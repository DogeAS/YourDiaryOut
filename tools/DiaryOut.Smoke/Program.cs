using DiaryOut.Core.Api;
using DiaryOut.Core.Export;

// 用法：dotnet run -- <email> <password> <outputDir>
// 冒烟测试：登录 → 全量同步 → 全格式导出 → 打印结果。凭据仅来自命令行参数，不落盘。
if (args.Length < 3)
{
    Console.WriteLine("usage: DiaryOut.Smoke <email> <password> <outputDir>");
    return 2;
}

using var client = new NiderijiClient();
Console.WriteLine("[1/3] 登录…");
var login = await client.LoginAsync(args[0], args[1]);
Console.WriteLine($"    成功：userid={login.UserId} name={login.UserConfig?.Name}");

Console.WriteLine("[2/3] 同步…");
var sync = await client.SyncAllAsync();
Console.WriteLine($"    日记 {sync.Diaries.Count} 篇，图片元数据 {sync.Images.Count} 条，" +
                  $"账号统计 diary_count={sync.UserConfig?.DiaryCount}");

Console.WriteLine("[3/3] 导出（HTML + Markdown + 单篇PDF + 合并PDF）…");
var options = new ExportOptions
{
    OutputDir = args[2],
    ExportHtml = true,
    ExportMarkdown = true,
    ExportPdfPerDiary = true,
    ExportMergedPdf = true,
};
var progress = new Progress<ExportProgress>(p => Console.WriteLine($"    {p.Message}"));
var result = await new ExportService().RunAsync(client, options, progress);

Console.WriteLine($"结果：导出 {result.Exported}，跳过 {result.Skipped}，失败 {result.Failures.Count}");
foreach (var f in result.Failures)
    Console.WriteLine($"    [失败] {f.Stage} {f.Title}：{f.Reason}");
return result.Failures.Count > 0 ? 1 : 0;
