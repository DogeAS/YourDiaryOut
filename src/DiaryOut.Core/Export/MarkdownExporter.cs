using System.Text;
using DiaryOut.Core.Parsing;

namespace DiaryOut.Core.Export;

/// <summary>
/// 单篇 Markdown 导出：标题、元数据、按行正文、本地图片引用。
/// 图片下载失败时使用引用块占位文字。
/// </summary>
public static class MarkdownExporter
{
    public static string Render(DiaryExportContext ctx)
    {
        var d = ctx.Diary;
        var sb = new StringBuilder();
        var title = string.IsNullOrWhiteSpace(d.Title) ? "无标题" : d.Title;

        sb.Append("# ").AppendLine(EscapeTitle(title));
        sb.AppendLine();
        sb.Append("> ").Append(d.CreatedDate).Append(' ')
          .Append(HtmlExporter.WeekdayCn(d.CreatedLocal)).Append(' ')
          .Append(d.CreatedLocal.ToString("HH:mm"));
        if (!string.IsNullOrWhiteSpace(d.Weather))
            sb.Append(" · 天气：").Append(d.Weather);
        if (!string.IsNullOrWhiteSpace(d.Mood))
            sb.Append(" · 心情：").Append(d.Mood);
        sb.Append(" · ").Append(DiaryContentParser.WordCount(d.Content)).Append(" 字");
        sb.AppendLine().AppendLine();

        foreach (var block in ctx.Blocks)
        {
            switch (block)
            {
                case TextBlock text:
                    if (string.IsNullOrWhiteSpace(text.Text))
                        sb.AppendLine();
                    else
                        sb.AppendLine(text.Text).AppendLine();
                    break;
                case TimeBlock time:
                    sb.Append("**").Append(time.Time).AppendLine("**").AppendLine();
                    break;
                case ImageBlock image:
                    if (ctx.ImagesFor(ExportFormat.Markdown).TryGetValue(image.ImageId, out var relPath))
                    {
                        sb.Append("![](").Append(relPath).AppendLine(")").AppendLine();
                    }
                    else
                    {
                        var reason = ctx.FailedImages.TryGetValue(image.ImageId, out var r) ? r : "未下载";
                        sb.Append("> [图片下载失败：id ").Append(image.ImageId)
                          .Append("，").Append(reason).AppendLine("]").AppendLine();
                    }
                    break;
            }
        }
        return sb.ToString();
    }

    private static string EscapeTitle(string title) => title.Replace("#", "\\#");
}
