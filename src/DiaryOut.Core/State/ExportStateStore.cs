using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DiaryOut.Core.Models;
using DiaryOut.Core.Parsing;

namespace DiaryOut.Core.State;

/// <summary>
/// 导出状态（state.json，存于输出目录）：实现断点续传与去重。
/// 按日记内容哈希判断变化——未变化则跳过，变化则重新导出该篇。
/// </summary>
public sealed class ExportStateStore
{
    public const string StateFileName = "state.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("entries")] public Dictionary<string, EntryState> Entries { get; set; } = new();

    public sealed class EntryState
    {
        [JsonPropertyName("hash")] public string Hash { get; set; } = "";
        [JsonPropertyName("folder")] public string Folder { get; set; } = "";
        [JsonPropertyName("exported_at")] public DateTime ExportedAt { get; set; }
    }

    public static ExportStateStore Load(string outputDir)
    {
        var path = Path.Combine(outputDir, StateFileName);
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<ExportStateStore>(File.ReadAllText(path), JsonOptions)
                       ?? new ExportStateStore();
        }
        catch
        {
            // 状态文件损坏时重新开始，不阻塞导出
        }
        return new ExportStateStore();
    }

    public void Save(string outputDir)
    {
        var path = Path.Combine(outputDir, StateFileName);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    /// <summary>内容哈希：标题 + 正文 + 最后修改 ts + 引用图片 id 列表。</summary>
    public static string ComputeHash(DiaryEntry diary, IReadOnlyList<ContentBlock> blocks)
    {
        var imageIds = string.Join(',', DiaryContentParser.ReferencedImageIds(blocks));
        var raw = $"{diary.Title}\n{diary.Content}\n{diary.Ts}\n{imageIds}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    /// <summary>该格式条目未变化（hash 一致）→ 可跳过。条目 key 形如 "markdown/123"。</summary>
    public bool IsUnchanged(string entryKey, string hash) =>
        Entries.TryGetValue(entryKey, out var state) && state.Hash == hash;
}
