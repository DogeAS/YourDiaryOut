using System.Text.Json.Serialization;

namespace DiaryOut.Core.Models;

/// <summary>POST /api/login/ 响应。</summary>
public sealed class LoginResponse
{
    [JsonPropertyName("error")] public int Error { get; set; }
    [JsonPropertyName("token")] public string? Token { get; set; }
    [JsonPropertyName("userid")] public long UserId { get; set; }
    [JsonPropertyName("user_config")] public UserConfig? UserConfig { get; set; }
}

/// <summary>POST /api/v2/sync/ 响应（diaries_ts=0 时为全量）。</summary>
public sealed class SyncResponse
{
    [JsonPropertyName("error")] public int Error { get; set; }
    [JsonPropertyName("diaries")] public List<DiaryEntry> Diaries { get; set; } = new();
    [JsonPropertyName("images")] public List<ImageInfo> Images { get; set; } = new();
    [JsonPropertyName("user_config")] public UserConfig? UserConfig { get; set; }
    [JsonPropertyName("version")] public string? Version { get; set; }
}

public sealed class DiaryEntry
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("user")] public long User { get; set; }
    [JsonPropertyName("createddate")] public string CreatedDate { get; set; } = "";
    [JsonPropertyName("createdtime")] public long CreatedTime { get; set; }
    [JsonPropertyName("ts")] public long Ts { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("content")] public string Content { get; set; } = "";
    [JsonPropertyName("weather")] public string Weather { get; set; } = "";
    [JsonPropertyName("mood")] public string Mood { get; set; } = "";
    [JsonPropertyName("mood_id")] public int? MoodId { get; set; }
    [JsonPropertyName("mood_color")] public string? MoodColor { get; set; }
    [JsonPropertyName("space")] public string Space { get; set; } = "";
    [JsonPropertyName("msg_count")] public int MsgCount { get; set; }

    /// <summary>创建时间（本地时区）。createdtime 为 Unix 秒。</summary>
    public DateTime CreatedLocal =>
        DateTimeOffset.FromUnixTimeSeconds(CreatedTime).LocalDateTime;
}

public sealed class ImageInfo
{
    [JsonPropertyName("image_id")] public long ImageId { get; set; }
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }

    /// <summary>保留未建模字段（服务端可能新增）。</summary>
    [JsonExtensionData] public Dictionary<string, System.Text.Json.JsonElement>? Extra { get; set; }
}

public sealed class UserConfig
{
    [JsonPropertyName("userid")] public long UserId { get; set; }
    [JsonPropertyName("useremail")] public string? UserEmail { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("diary_count")] public int DiaryCount { get; set; }
    [JsonPropertyName("word_count")] public int WordCount { get; set; }
    [JsonPropertyName("image_count")] public int ImageCount { get; set; }
}
