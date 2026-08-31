using System.Text.Json;
using DiaryOut.Core.Api;
using DiaryOut.Core.Models;
using DiaryOut.Core.Parsing;
using DiaryOut.Core.State;

namespace DiaryOut.Core.Export;

/// <summary>
/// 导出编排：同步 → 过滤 → 去重（state.json）→ 下载图片 → 写出 HTML/Markdown/PDF
/// → index.json + failures.json。单篇失败不中断，整体结束后生成失败清单。
/// </summary>
public sealed class ExportService
{
    public const string IndexFileName = "index.json";
    public const string FailuresFileName = "failures.json";
    public const string MergedPdfName = "日记合集.pdf";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<ExportResult> RunAsync(
        NiderijiClient client,
        ExportOptions options,
        IProgress<ExportProgress>? progress = null,
        CancellationToken ct = default)
    {
        client.MinRequestInterval = TimeSpan.FromMilliseconds(options.MinRequestIntervalMs);
        client.MaxRetries = options.MaxRetries;

        var result = new ExportResult { OutputDir = options.OutputDir };
        Directory.CreateDirectory(options.OutputDir);

        Report(progress, 0, 0, "正在同步日记数据…");
        var sync = await client.SyncAllAsync(ct).ConfigureAwait(false);

        var diaries = Filter(sync.Diaries, options);
        var state = ExportStateStore.Load(options.OutputDir);
        var indexEntries = new List<object>();

        Report(progress, diaries.Count, 0, $"共 {diaries.Count} 篇日记待处理");
        var done = 0;

        // 合并 PDF 用：收集所有（含未变化跳过的）日记上下文
        var mergedContexts = new List<DiaryExportContext>();

        foreach (var diary in diaries)
        {
            ct.ThrowIfCancellationRequested();
            done++;
            var title = string.IsNullOrWhiteSpace(diary.Title) ? "无标题" : diary.Title;
            Report(progress, diaries.Count, done, $"[{done}/{diaries.Count}] {diary.CreatedDate} {title}");

            try
            {
                var blocks = DiaryContentParser.Parse(diary.Content);
                var hash = ExportStateStore.ComputeHash(diary, blocks);
                var baseName = FileNameHelper.DiaryFolderName(diary);

                var ctx = new DiaryExportContext { Diary = diary, Blocks = blocks, BaseName = baseName };

                // 一次性下载该篇全部图片字节（各格式共用，避免重复请求）
                await DownloadImagesAsync(client, diary, ctx, result, ct).ConfigureAwait(false);

                // 各格式独立判断"是否未变化"并导出到各自子目录，互不干扰
                var anyExported = false;
                if (options.ExportMarkdown)
                    anyExported |= ExportOneFormat(client, ctx, options, state,
                        ExportFormat.Markdown, options.MarkdownFolderName, "diary.md",
                        (c, path) => File.WriteAllText(path, MarkdownExporter.Render(c)));

                if (options.ExportHtml)
                    anyExported |= ExportOneFormat(client, ctx, options, state,
                        ExportFormat.Html, options.HtmlFolderName, "diary.html",
                        (c, path) => File.WriteAllText(path, HtmlExporter.Render(c)));

                if (options.ExportPdfPerDiary)
                    anyExported |= ExportOneFormat(client, ctx, options, state,
                        ExportFormat.Pdf, options.PdfFolderName, "diary.pdf",
                        (c, path) => PdfExporter.RenderPerDiary(c, path));

                if (options.ExportMergedPdf)
                {
                    // 合并 PDF 直接封装字节，需保证 DownloadedImages 有数据；
                    // 若某些格式被跳过（未下载到内存），此处补下载。
                    await DownloadImagesAsync(client, diary, ctx, result, ct).ConfigureAwait(false);
                    mergedContexts.Add(ctx);
                }

                if (anyExported) result.Exported++; else result.Skipped++;

                indexEntries.Add(new
                {
                    id = diary.Id,
                    date = diary.CreatedDate,
                    time = diary.CreatedLocal.ToString("HH:mm"),
                    title = diary.Title,
                    name = baseName,
                    word_count = DiaryContentParser.WordCount(diary.Content),
                    image_count = DiaryContentParser.ReferencedImageIds(blocks).Count,
                    weather = diary.Weather,
                    mood = diary.Mood,
                });
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                result.Failures.Add(new FailureItem
                {
                    DiaryId = diary.Id,
                    Title = title,
                    Stage = "日记导出",
                    Reason = ex.Message,
                });
            }
        }

        if (options.ExportMergedPdf && mergedContexts.Count > 0)
        {
            Report(progress, diaries.Count, done, "正在生成合并 PDF…");
            try
            {
                var pdfDir = Path.Combine(options.OutputDir, options.PdfFolderName);
                Directory.CreateDirectory(pdfDir);
                PdfExporter.RenderMerged(mergedContexts, Path.Combine(pdfDir, MergedPdfName));
            }
            catch (Exception ex)
            {
                result.Failures.Add(new FailureItem { Stage = "合并 PDF", Reason = ex.Message });
            }
        }

        if (PdfExporter.FontWarning is { } fontWarning)
            result.Failures.Add(new FailureItem { Stage = "PDF 字体", Reason = fontWarning });

        File.WriteAllText(Path.Combine(options.OutputDir, IndexFileName),
            JsonSerializer.Serialize(indexEntries, JsonOptions));
        File.WriteAllText(Path.Combine(options.OutputDir, FailuresFileName),
            JsonSerializer.Serialize(result.Failures, JsonOptions));
        state.Save(options.OutputDir);

        Report(progress, diaries.Count, done,
            $"完成：导出 {result.Exported} 篇，跳过 {result.Skipped} 篇，失败 {result.Failures.Count} 项");
        return result;
    }

    /// <summary>
    /// 导出单个格式的一篇日记。该格式的内容未变化且文件齐全 → 跳过；
    /// 否则（重新）写出正文并填充该格式的 images 目录。返回是否实际导出。
    /// </summary>
    private static bool ExportOneFormat(
        NiderijiClient client, DiaryExportContext ctx, ExportOptions options,
        ExportStateStore state, ExportFormat format, string formatFolderName,
        string fileName, Action<DiaryExportContext, string> writer)
    {
        var formatDir = Path.Combine(options.OutputDir, formatFolderName);
        var formatKey = $"{formatFolderName}/{ctx.Diary.Id}";
        var hash = ExportStateStore.ComputeHash(ctx.Diary, ctx.Blocks);
        var targetFile = Path.Combine(formatDir, ctx.BaseName + Path.GetExtension(fileName));
        var imagesDir = Path.Combine(formatDir, "images");

        // 未变化且文件与图片齐全 → 跳过
        if (state.IsUnchanged(formatKey, hash)
            && File.Exists(targetFile)
            && ImagesPresent(ctx, format, imagesDir))
        {
            RestoreImageMap(ctx, format, imagesDir);
            return false;
        }

        Directory.CreateDirectory(formatDir);
        Directory.CreateDirectory(imagesDir);
        EnsureUniqueTextFile(ref targetFile);

        // 填充该格式的图片映射（写盘仅针对文本格式；PDF 用字节缓存封装）
        PopulateFormatImages(ctx, format, imagesDir);

        writer(ctx, targetFile);

        state.Entries[formatKey] = new ExportStateStore.EntryState
        {
            Hash = hash,
            Folder = formatFolderName,
            ExportedAt = DateTime.Now,
        };
        return true;
    }

    /// <summary>把已下载的图片字节写入某格式的 images 目录，并登记相对路径映射。</summary>
    private static void PopulateFormatImages(DiaryExportContext ctx, ExportFormat format, string imagesDir)
    {
        var map = ctx.ImagesFor(format);
        map.Clear();
        foreach (var imageId in DiaryContentParser.ReferencedImageIds(ctx.Blocks))
        {
            if (ctx.DownloadedImages.TryGetValue(imageId, out var img))
            {
                var fileName = $"{imageId}{img.Ext}";
                File.WriteAllBytes(Path.Combine(imagesDir, fileName), img.Data);
                map[imageId] = $"images/{fileName}";
            }
            // 下载失败的图片不进入 map，由导出器渲染占位文字
        }
    }

    /// <summary>该格式 images 目录是否已包含全部所需图片。</summary>
    private static bool ImagesPresent(DiaryExportContext ctx, ExportFormat format, string imagesDir)
    {
        foreach (var imageId in DiaryContentParser.ReferencedImageIds(ctx.Blocks))
        {
            if (ctx.FailedImages.ContainsKey(imageId))
                continue; // 本来就下载失败的，不要求存在
            var exists = Directory.Exists(imagesDir)
                && Directory.EnumerateFiles(imagesDir, $"{imageId}.*").Any();
            if (!exists) return false;
        }
        return true;
    }

    /// <summary>跳过场景：从磁盘 images 目录恢复该格式的相对路径映射（供后续合并 PDF 等使用）。</summary>
    private static void RestoreImageMap(DiaryExportContext ctx, ExportFormat format, string imagesDir)
    {
        var map = ctx.ImagesFor(format);
        foreach (var imageId in DiaryContentParser.ReferencedImageIds(ctx.Blocks))
        {
            if (map.ContainsKey(imageId)) continue;
            var file = Directory.Exists(imagesDir)
                ? Directory.EnumerateFiles(imagesDir, $"{imageId}.*").FirstOrDefault()
                : null;
            if (file is not null)
                map[imageId] = $"images/{Path.GetFileName(file)}";
        }
    }

    /// <summary>若同名文件已存在且属于其它日记，则追加编号避免覆盖。</summary>
    private static void EnsureUniqueTextFile(ref string targetFile)
    {
        // 同名日记（不同 id）会撞文件名；state 已按 formatKey 记录，这里简单按存在性追加编号
        if (!File.Exists(targetFile)) return;
        var dir = Path.GetDirectoryName(targetFile)!;
        var stem = Path.GetFileNameWithoutExtension(targetFile);
        var ext = Path.GetExtension(targetFile);
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate)) { targetFile = candidate; return; }
        }
    }

    /// <summary>下载该篇全部图片到内存缓存（ctx.DownloadedImages），供各格式共用，避免重复请求。</summary>
    private async Task DownloadImagesAsync(
        NiderijiClient client, DiaryEntry diary, DiaryExportContext ctx,
        ExportResult result, CancellationToken ct)
    {
        foreach (var imageId in DiaryContentParser.ReferencedImageIds(ctx.Blocks))
        {
            ct.ThrowIfCancellationRequested();
            if (ctx.DownloadedImages.ContainsKey(imageId) || ctx.FailedImages.ContainsKey(imageId))
                continue;
            var downloaded = await client.DownloadImageAsync(diary.User, imageId, ct).ConfigureAwait(false);
            if (downloaded is { } image)
            {
                ctx.DownloadedImages[imageId] = image;
            }
            else
            {
                const string reason = "下载失败（重试后仍失败）";
                ctx.FailedImages[imageId] = reason;
                result.Failures.Add(new FailureItem
                {
                    DiaryId = diary.Id,
                    Title = diary.Title,
                    Stage = "图片下载",
                    Reason = $"image_id {imageId}：{reason}",
                });
            }
        }
    }

    private static List<DiaryEntry> Filter(IEnumerable<DiaryEntry> diaries, ExportOptions options)
    {
        var query = diaries;

        if (options.FromDate is { } from || options.ToDate is { } to)
        {
            query = query.Where(d =>
                DateOnly.TryParse(d.CreatedDate, out var date)
                && (options.FromDate is not { } f || date >= f)
                && (options.ToDate is not { } t || date <= t));
        }

        if (!string.IsNullOrWhiteSpace(options.Keyword))
        {
            var keyword = options.Keyword.Trim();
            query = query.Where(d =>
                d.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || d.Content.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        if (options.SelectedIds is { Count: > 0 } selected)
            query = query.Where(d => selected.Contains(d.Id));

        return query.OrderBy(d => d.CreatedTime).ThenBy(d => d.Id).ToList();
    }

    private static void Report(IProgress<ExportProgress>? progress, int total, int done, string message) =>
        progress?.Report(new ExportProgress { Total = total, Done = done, Message = message });
}
