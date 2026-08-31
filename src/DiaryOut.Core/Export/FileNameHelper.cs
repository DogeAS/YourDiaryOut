using System.Text;

namespace DiaryOut.Core.Export;

/// <summary>目录/文件名工具：清理 Windows 非法字符，重名追加编号。</summary>
public static class FileNameHelper
{
    private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();
    private const int MaxNameLength = 60;

    public static string Sanitize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "未命名";

        var sb = new StringBuilder(name.Length);
        foreach (var c in name.Trim())
            sb.Append(InvalidChars.Contains(c) ? '_' : c);

        var cleaned = sb.ToString().TrimEnd('.', ' ');
        if (cleaned.Length > MaxNameLength)
            cleaned = cleaned[..MaxNameLength].TrimEnd('.', ' ');
        return string.IsNullOrEmpty(cleaned) ? "未命名" : cleaned;
    }

    /// <summary>日记目录名：日期-标题。</summary>
    public static string DiaryFolderName(Models.DiaryEntry diary) =>
        $"{Sanitize(diary.CreatedDate)}-{Sanitize(diary.Title)}";

    /// <summary>在目录下取得不冲突的子目录名，重名时追加 " (2)"、" (3)"…。</summary>
    public static string EnsureUniqueFolder(string parentDir, string desiredName)
    {
        var candidate = desiredName;
        for (var i = 2; Directory.Exists(Path.Combine(parentDir, candidate)); i++)
            candidate = $"{desiredName} ({i})";
        return candidate;
    }
}
