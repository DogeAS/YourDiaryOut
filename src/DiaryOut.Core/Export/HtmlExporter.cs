using System.Net;
using System.Text;
using DiaryOut.Core.Parsing;

namespace DiaryOut.Core.Export;

/// <summary>
/// 单篇 HTML 导出：保留内容结构，使用独立简洁样式（不依赖站点资源，可离线查看）。
/// 图片引用本地 images/ 目录；下载失败的图片显示占位文字。
/// </summary>
public static class HtmlExporter
{
    private const string Css = """
        body { font-family: "Microsoft YaHei", "PingFang SC", sans-serif; max-width: 720px;
               margin: 2em auto; padding: 0 1em; color: #333; line-height: 1.8; }
        h1 { font-size: 1.5em; border-bottom: 1px solid #ddd; padding-bottom: .4em; }
        .meta { color: #888; font-size: .9em; margin-bottom: 1.5em; }
        .meta span { margin-right: 1em; }
        .time { color: #aaa; font-size: .85em; margin-top: 1.2em; }
        p { margin: .3em 0; white-space: pre-wrap; }
        .photo { text-align: center; margin: 1em 0; }
        .photo img { max-width: 100%; border-radius: 4px; }
        .photo-failed { color: #b00; background: #fbeaea; border: 1px dashed #d99;
                        padding: .6em; text-align: center; margin: 1em 0; font-size: .9em; }
        """;

    public static string Render(DiaryExportContext ctx)
    {
        var d = ctx.Diary;
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\">")
          .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
          .Append("<title>").Append(Escape(TitleOrDefault(d))).Append("</title>")
          .Append("<style>").Append(Css).Append("</style></head><body>");

        sb.Append("<h1>").Append(Escape(TitleOrDefault(d))).Append("</h1>");
        sb.Append("<div class=\"meta\">")
          .Append("<span>").Append(Escape(d.CreatedDate)).Append(' ')
          .Append(Escape(WeekdayCn(d.CreatedLocal))).Append(' ')
          .Append(Escape(d.CreatedLocal.ToString("HH:mm"))).Append("</span>");
        if (!string.IsNullOrWhiteSpace(d.Weather))
            sb.Append("<span>天气：").Append(Escape(d.Weather)).Append("</span>");
        if (!string.IsNullOrWhiteSpace(d.Mood))
            sb.Append("<span>心情：").Append(Escape(d.Mood)).Append("</span>");
        sb.Append("<span>").Append(WordCount(d)).Append(" 字</span></div>");

        foreach (var block in ctx.Blocks)
        {
            switch (block)
            {
                case TextBlock text:
                    sb.Append("<p>").Append(Escape(text.Text)).Append("</p>");
                    break;
                case TimeBlock time:
                    sb.Append("<div class=\"time\">").Append(Escape(time.Time)).Append("</div>");
                    break;
                case ImageBlock image:
                    if (ctx.ImagesFor(ExportFormat.Html).TryGetValue(image.ImageId, out var relPath))
                    {
                        sb.Append("<div class=\"photo\"><img src=\"")
                          .Append(Escape(relPath)).Append("\" alt=\"图片 ")
                          .Append(image.ImageId).Append("\"></div>");
                    }
                    else
                    {
                        var reason = ctx.FailedImages.TryGetValue(image.ImageId, out var r) ? r : "未下载";
                        sb.Append("<div class=\"photo-failed\">[图片下载失败：id ")
                          .Append(image.ImageId).Append("，").Append(Escape(reason)).Append("]</div>");
                    }
                    break;
            }
        }

        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static string TitleOrDefault(Models.DiaryEntry d) =>
        string.IsNullOrWhiteSpace(d.Title) ? "无标题" : d.Title;

    private static int WordCount(Models.DiaryEntry d) => DiaryContentParser.WordCount(d.Content);

    private static string Escape(string? s) => WebUtility.HtmlEncode(s ?? "");

    public static string WeekdayCn(DateTime dt) => dt.DayOfWeek switch
    {
        DayOfWeek.Monday => "星期一",
        DayOfWeek.Tuesday => "星期二",
        DayOfWeek.Wednesday => "星期三",
        DayOfWeek.Thursday => "星期四",
        DayOfWeek.Friday => "星期五",
        DayOfWeek.Saturday => "星期六",
        _ => "星期日",
    };
}
