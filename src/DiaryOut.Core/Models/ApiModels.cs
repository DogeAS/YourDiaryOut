using System.Text.Json;
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

    /// <summary>宽高兼容数字或字符串（如 128 或 "128"），失败回退 null。</summary>
    [JsonPropertyName("width")]
    [JsonConverter(typeof(FlexibleIntConverter))]
    public int? Width { get; set; }

    [JsonPropertyName("height")]
    [JsonConverter(typeof(FlexibleIntConverter))]
    public int? Height { get; set; }

    /// <summary>保留未建模字段（服务端可能新增）。</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>接受 JSON 整数、小数（820.0）或数字字符串的可空 int 转换器。</summary>
public sealed class FlexibleIntConverter : JsonConverter<int?>
{
    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Number when reader.TryGetInt32(out var n) => n,
            JsonTokenType.Number when reader.TryGetDouble(out var dbl) => (int)Math.Round(dbl),
            JsonTokenType.String when double.TryParse(reader.GetString(), out var s) => (int)Math.Round(s),
            JsonTokenType.Null => null,
            _ => null,
        };

    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        if (value.HasValue) writer.WriteNumberValue(value.Value);
        else writer.WriteNullValue();
    }
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
