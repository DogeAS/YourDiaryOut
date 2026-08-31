namespace DiaryOut.Core.Export;

/// <summary>导出选项（对应需求基线）。</summary>
public sealed class ExportOptions
{
    /// <summary>输出根目录。</summary>
    public required string OutputDir { get; set; }

    /// <summary>日期范围（含当天，本地日期）。null 表示不限。</summary>
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }

    /// <summary>关键词过滤（匹配标题与正文，忽略大小写）。null/空 表示不过滤。</summary>
    public string? Keyword { get; set; }

    /// <summary>手动选择的日记 id 集合；null 表示不按选择过滤。</summary>
    public HashSet<long>? SelectedIds { get; set; }

    public bool ExportHtml { get; set; } = true;
    public bool ExportMarkdown { get; set; } = true;
    /// <summary>每篇日记一个 PDF。</summary>
    public bool ExportPdfPerDiary { get; set; }
    /// <summary>全部导出日记合并为一个 PDF。</summary>
    public bool ExportMergedPdf { get; set; }

    /// <summary>请求最小间隔（毫秒，限速）。</summary>
    public int MinRequestIntervalMs { get; set; } = 500;
    /// <summary>失败重试次数（指数退避）。</summary>
    public int MaxRetries { get; set; } = 3;
}

public sealed class FailureItem
{
    public long DiaryId { get; set; }
    public string Title { get; set; } = "";
    public string Stage { get; set; } = "";
    public string Reason { get; set; } = "";
}

public sealed class ExportProgress
{
    public int Total { get; set; }
    public int Done { get; set; }
    public string Message { get; set; } = "";
}

public sealed class ExportResult
{
    public int Exported { get; set; }
    public int Skipped { get; set; }
    public List<FailureItem> Failures { get; } = new();
    public string OutputDir { get; set; } = "";
}

/// <summary>单篇日记处理上下文（解析结果 + 已下载图片）。</summary>
public sealed class DiaryExportContext
{
    public required Models.DiaryEntry Diary { get; init; }
    public required IReadOnlyList<Parsing.ContentBlock> Blocks { get; init; }

    /// <summary>该篇日记的输出目录（绝对路径），由 ExportService 赋值。</summary>
    public string Folder { get; set; } = "";

    /// <summary>image_id → 本地图片相对路径（如 images/123.jpg）；下载失败则无此项。</summary>
    public Dictionary<long, string> LocalImages { get; } = new();

    /// <summary>image_id → 下载失败原因。</summary>
    public Dictionary<long, string> FailedImages { get; } = new();
}
