using System.Text.RegularExpressions;
using DiaryOut.Core.Models;

namespace DiaryOut.Core.Parsing;

/// <summary>正文内容块。</summary>
public abstract record ContentBlock;

/// <summary>普通文本行。</summary>
public sealed record TextBlock(string Text) : ContentBlock;

/// <summary>时间分段标记，如 [11:47]。</summary>
public sealed record TimeBlock(string Time) : ContentBlock;

/// <summary>图片占位符，如 [图123]，对应 images 元数据中的 image_id。</summary>
public sealed record ImageBlock(long ImageId) : ContentBlock;

/// <summary>
/// 将站点正文格式解析为内容块序列。
/// 站点行为（来自前端 JS）：
/// - 行内 "[图" + 数字 + "]" 表示图片，按 image_id 匹配 images 元数据；
/// - 行内 "[HH:mm]" 为时间分段标记；
/// - 其余为普通文本，按行保留换行。
/// </summary>
public static partial class DiaryContentParser
{
    [GeneratedRegex(@"^\[图([^\]]*)\]\s*$")]
    private static partial Regex ImageLineRegex();

    [GeneratedRegex(@"^\[(\d{1,2}:\d{2}(?::\d{2})?)\]\s*$")]
    private static partial Regex TimeLineRegex();

    public static IReadOnlyList<ContentBlock> Parse(string? content)
    {
        var blocks = new List<ContentBlock>();
        if (string.IsNullOrEmpty(content))
            return blocks;

        foreach (var rawLine in content.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            var imageMatch = ImageLineRegex().Match(line);
            if (imageMatch.Success && long.TryParse(imageMatch.Groups[1].Value, out var imageId))
            {
                blocks.Add(new ImageBlock(imageId));
                continue;
            }

            var timeMatch = TimeLineRegex().Match(line);
            if (timeMatch.Success)
            {
                blocks.Add(new TimeBlock(timeMatch.Groups[1].Value));
                continue;
            }

            blocks.Add(new TextBlock(line));
        }
        return blocks;
    }

    /// <summary>提取正文引用的全部图片 id（按出现顺序去重）。</summary>
    public static IReadOnlyList<long> ReferencedImageIds(IReadOnlyList<ContentBlock> blocks) =>
        blocks.OfType<ImageBlock>().Select(b => b.ImageId).Distinct().ToList();

    /// <summary>纯文本字数（与站点口径一致：去除全部空白字符）。</summary>
    public static int WordCount(string? content) =>
        string.IsNullOrEmpty(content) ? 0 : content.Count(c => !char.IsWhiteSpace(c));
}
