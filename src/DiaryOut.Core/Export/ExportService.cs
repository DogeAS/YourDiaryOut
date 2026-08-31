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
        var contexts = new List<DiaryExportContext>();
        var indexEntries = new List<object>();

        Report(progress, diaries.Count, 0, $"共 {diaries.Count} 篇日记待处理");
        var done = 0;

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

                if (state.IsUnchanged(diary, hash, options.OutputDir))
                {
                    result.Skipped++;
                    var existing = state.Entries[diary.Id.ToString()];
                    indexEntries.Add(IndexEntry(diary, existing.Folder, blocks));

                    // 未变化的日记也要纳入合并 PDF：重建上下文并从既有目录恢复图片映射
                    if (options.ExportMergedPdf)
                        contexts.Add(RebuildContextFromDisk(diary, blocks,
                            Path.Combine(options.OutputDir, existing.Folder)));
                    continue;
                }

                var folderName = FileNameHelper.EnsureUniqueFolder(
                    options.OutputDir, FileNameHelper.DiaryFolderName(diary));
                var folder = Path.Combine(options.OutputDir, folderName);
                var imagesDir = Path.Combine(folder, "images");
                Directory.CreateDirectory(imagesDir);

                var ctx = new DiaryExportContext { Diary = diary, Blocks = blocks, Folder = folder };

                await DownloadImagesAsync(client, diary, ctx, imagesDir, result, ct).ConfigureAwait(false);
                WriteDiaryFiles(ctx, folder, options);

                contexts.Add(ctx);
                state.Entries[diary.Id.ToString()] = new ExportStateStore.EntryState
                {
                    Hash = hash,
                    Folder = folderName,
                    ExportedAt = DateTime.Now,
                };
                indexEntries.Add(IndexEntry(diary, folderName, blocks));
                result.Exported++;
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

        if (options.ExportMergedPdf && contexts.Count > 0)
        {
            Report(progress, diaries.Count, done, "正在生成合并 PDF…");
            try
            {
                PdfExporter.RenderMerged(contexts, Path.Combine(options.OutputDir, MergedPdfName));
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

    private async Task DownloadImagesAsync(
        NiderijiClient client, DiaryEntry diary, DiaryExportContext ctx,
        string imagesDir, ExportResult result, CancellationToken ct)
    {
        foreach (var imageId in DiaryContentParser.ReferencedImageIds(ctx.Blocks))
        {
            ct.ThrowIfCancellationRequested();
            var downloaded = await client.DownloadImageAsync(diary.User, imageId, ct).ConfigureAwait(false);
            if (downloaded is { } image)
            {
                var relPath = $"images/{imageId}{image.Extension}";
                await File.WriteAllBytesAsync(Path.Combine(imagesDir, $"{imageId}{image.Extension}"),
                    image.Data, ct).ConfigureAwait(false);
                ctx.LocalImages[imageId] = relPath;
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

    private static void WriteDiaryFiles(DiaryExportContext ctx, string folder, ExportOptions options)
    {
        if (options.ExportHtml)
            File.WriteAllText(Path.Combine(folder, "diary.html"), HtmlExporter.Render(ctx));
        if (options.ExportMarkdown)
            File.WriteAllText(Path.Combine(folder, "diary.md"), MarkdownExporter.Render(ctx));
        if (options.ExportPdfPerDiary)
            PdfExporter.RenderPerDiary(ctx, Path.Combine(folder, "diary.pdf"));
    }

    /// <summary>为已跳过（未变化）的日记重建上下文：扫描既有 images 目录恢复本地图片映射。</summary>
    private static DiaryExportContext RebuildContextFromDisk(
        DiaryEntry diary, IReadOnlyList<ContentBlock> blocks, string folder)
    {
        var ctx = new DiaryExportContext { Diary = diary, Blocks = blocks, Folder = folder };
        var imagesDir = Path.Combine(folder, "images");
        foreach (var imageId in DiaryContentParser.ReferencedImageIds(blocks))
        {
            var file = Directory.Exists(imagesDir)
                ? Directory.EnumerateFiles(imagesDir, $"{imageId}.*").FirstOrDefault()
                : null;
            if (file is not null)
                ctx.LocalImages[imageId] = $"images/{Path.GetFileName(file)}";
            else
                ctx.FailedImages[imageId] = "本地图片文件缺失";
        }
        return ctx;
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

    private static object IndexEntry(DiaryEntry diary, string folder, IReadOnlyList<ContentBlock> blocks) => new
    {
        id = diary.Id,
        date = diary.CreatedDate,
        time = diary.CreatedLocal.ToString("HH:mm"),
        title = diary.Title,
        folder,
        word_count = DiaryContentParser.WordCount(diary.Content),
        image_count = DiaryContentParser.ReferencedImageIds(blocks).Count,
        weather = diary.Weather,
        mood = diary.Mood,
    };

    private static void Report(IProgress<ExportProgress>? progress, int total, int done, string message) =>
        progress?.Report(new ExportProgress { Total = total, Done = done, Message = message });
}
