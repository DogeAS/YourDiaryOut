using DiaryOut.Core.Parsing;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace DiaryOut.Core.Export;

/// <summary>
/// PDF 导出（QuestPDF）：支持单篇 PDF 与合并 PDF。
/// 中文字体：优先注册微软雅黑，失败则退回宋体，再失败使用默认字体并记录警告。
/// </summary>
public static class PdfExporter
{
    private static string? _fontFamily;
    private static bool _fontInitDone;

    /// <summary>字体注册失败时的警告信息（无警告为 null）。</summary>
    public static string? FontWarning { get; private set; }

    public static void RenderPerDiary(DiaryExportContext ctx, string filePath)
    {
        EnsureInitialized();
        BuildDocument(new[] { ctx }).GeneratePdf(filePath);
    }

    public static void RenderMerged(IReadOnlyList<DiaryExportContext> contexts, string filePath)
    {
        EnsureInitialized();
        BuildDocument(contexts).GeneratePdf(filePath);
    }

    /// <summary>PDF 图片直接封装进文件：从原始字节缓存读取，不依赖磁盘图片目录。</summary>
    private static byte[]? GetImageBytes(DiaryExportContext ctx, long imageId) =>
        ctx.DownloadedImages.TryGetValue(imageId, out var img) ? img.Data : null;

    private static void EnsureInitialized()
    {
        if (_fontInitDone)
            return;
        _fontInitDone = true;

        QuestPDF.Settings.License = LicenseType.Community;

        foreach (var (path, family) in new[]
        {
            (@"C:\Windows\Fonts\msyh.ttc", "Microsoft YaHei"),
            (@"C:\Windows\Fonts\simsun.ttc", "SimSun"),
        })
        {
            try
            {
                if (!File.Exists(path))
                    continue;
                using var stream = File.OpenRead(path);
                FontManager.RegisterFont(stream);
                _fontFamily = family;
                return;
            }
            catch (Exception ex)
            {
                FontWarning = $"中文字体 {family} 注册失败：{ex.Message}";
            }
        }

        if (_fontFamily is null)
            FontWarning ??= "未找到可用中文字体（微软雅黑/宋体），PDF 中文可能无法正常显示";
    }

    private static IDocument BuildDocument(IReadOnlyList<DiaryExportContext> contexts) =>
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                var defaultStyle = TextStyle.Default.FontSize(11).LineHeight(1.6f);
                if (_fontFamily is not null)
                    defaultStyle = defaultStyle.FontFamily(_fontFamily);
                page.DefaultTextStyle(_ => defaultStyle);

                page.Footer().AlignCenter().Text(t => t.CurrentPageNumber());

                page.Content().Column(column =>
                {
                    column.Spacing(6);
                    for (var i = 0; i < contexts.Count; i++)
                    {
                        if (i > 0)
                            column.Item().PageBreak();
                        ComposeDiary(column, contexts[i]);
                    }
                });
            });
        });

    private static void ComposeDiary(ColumnDescriptor column, DiaryExportContext ctx)
    {
        var d = ctx.Diary;
        var title = string.IsNullOrWhiteSpace(d.Title) ? "无标题" : d.Title;

        column.Item().Text(title).FontSize(18).Bold();

        var meta = $"{d.CreatedDate} {HtmlExporter.WeekdayCn(d.CreatedLocal)} {d.CreatedLocal:HH:mm}"
                   + (string.IsNullOrWhiteSpace(d.Weather) ? "" : $" · 天气：{d.Weather}")
                   + (string.IsNullOrWhiteSpace(d.Mood) ? "" : $" · 心情：{d.Mood}")
                   + $" · {DiaryContentParser.WordCount(d.Content)} 字";
        column.Item().PaddingBottom(8).Text(meta).FontSize(9).FontColor(Colors.Grey.Darken1);
        column.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);

        foreach (var block in ctx.Blocks)
        {
            switch (block)
            {
                case TextBlock text:
                    column.Item().Text(text.Text);
                    break;
                case TimeBlock time:
                    column.Item().PaddingTop(6).Text(time.Time).FontSize(9)
                        .FontColor(Colors.Grey.Medium);
                    break;
                case ImageBlock image:
                    var imgBytes = GetImageBytes(ctx, image.ImageId);
                    if (imgBytes is not null)
                    {
                        column.Item().PaddingVertical(4).MaxWidth(420).Image(imgBytes);
                        break;
                    }
                    var reason = ctx.FailedImages.TryGetValue(image.ImageId, out var r) ? r : "未下载";
                    column.Item().Padding(6).Background(Colors.Red.Lighten5)
                        .Text($"[图片下载失败：id {image.ImageId}，{reason}]")
                        .FontSize(9).FontColor(Colors.Red.Darken2);
                    break;
            }
        }
    }
}
