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
    /// <summary>每篇日记一个 PDF（图片封装在 PDF 内，不单独存图片）。</summary>
    public bool ExportPdfPerDiary { get; set; }
    /// <summary>全部导出日记合并为一个 PDF（图片封装在 PDF 内）。</summary>
    public bool ExportMergedPdf { get; set; }

    /// <summary>请求最小间隔（毫秒，限速）。</summary>
    public int MinRequestIntervalMs { get; set; } = 500;
    /// <summary>失败重试次数（指数退避）。</summary>
    public int MaxRetries { get; set; } = 3;

    // ---- 分格式子目录名 ----
    public string MarkdownFolderName { get; set; } = "markdown";
    public string HtmlFolderName { get; set; } = "html";
    public string PdfFolderName { get; set; } = "pdf";
}

/// <summary>导出的内容格式。</summary>
public enum ExportFormat { Markdown, Html, Pdf }

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

/// <summary>
/// 单篇日记处理上下文（解析结果 + 各格式的本地图片引用）。
/// 每种格式（Markdown/Html/Pdf）有独立的图片目录与相对路径映射，互不影响，
/// 因此同一篇的不同格式不会因彼此而误判"未变化"。
/// </summary>
public sealed class DiaryExportContext
{
    public required Models.DiaryEntry Diary { get; init; }
    public required IReadOnlyList<Parsing.ContentBlock> Blocks { get; init; }

    /// <summary>该篇日记名（"日期-标题"），用于各格式目录下的文件名。</summary>
    public string BaseName { get; set; } = "";

    /// <summary>
    /// 每个格式的独立图片映射：image_id → 该格式目录内的相对路径（如 "images/123.jpg"）。
    /// PDF 也存（其图片会先落盘到 pdf/images 供封装，见 ExportService）。
    /// </summary>
    public Dictionary<ExportFormat, Dictionary<long, string>> LocalImages { get; } = new()
    {
        [ExportFormat.Markdown] = new(),
        [ExportFormat.Html] = new(),
        [ExportFormat.Pdf] = new(),
    };

    /// <summary>image_id → 下载失败原因（下载一次，供所有格式复用）。</summary>
    public Dictionary<long, string> FailedImages { get; } = new();

    /// <summary>下载成功后的原始字节缓存：image_id → (bytes, ext)，供多格式共用，避免重复下载。</summary>
    public Dictionary<long, (byte[] Data, string Ext)> DownloadedImages { get; } = new();

    public Dictionary<long, string> ImagesFor(ExportFormat format) => LocalImages[format];
}
