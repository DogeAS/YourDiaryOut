using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DiaryOut.Core.Api;
using DiaryOut.Core.Export;
using DiaryOut.Core.Models;

namespace DiaryOut.App;

/// <summary>
/// 主窗口：登录 → 配置导出范围/格式 → 后台导出（断点续传、去重、失败清单）。
/// </summary>
public partial class MainWindow : Window
{
    private sealed record DiaryListItem(DiaryEntry Diary)
    {
        public string Display => $"{Diary.CreatedDate}  {(string.IsNullOrWhiteSpace(Diary.Title) ? "无标题" : Diary.Title)}";
    }

    private readonly NiderijiClient _client = new();
    private readonly ExportService _exportService = new();
    private List<DiaryListItem> _diaryItems = new();
    private CancellationTokenSource? _exportCts;
    private string? _lastOutputDir;

    public MainWindow()
    {
        InitializeComponent();
        OutputDirBox.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DiaryOut");
    }

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            LoginButton_Click(sender, e);
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        var email = EmailBox.Text.Trim();
        var password = PasswordBox.Password;
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            LoginStatusText.Text = "请输入账号和密码";
            return;
        }

        LoginButton.IsEnabled = false;
        LoginStatusText.Foreground = System.Windows.Media.Brushes.Gray;
        LoginStatusText.Text = "正在登录…";
        try
        {
            await _client.LoginAsync(email, password);
            PasswordBox.Clear();

            var sync = await _client.SyncAllAsync();
            EnterExportPanel(sync);
        }
        catch (NiderijiApiException ex)
        {
            LoginStatusText.Foreground = System.Windows.Media.Brushes.DarkRed;
            LoginStatusText.Text = ex.Message;
        }
        catch (Exception ex)
        {
            LoginStatusText.Foreground = System.Windows.Media.Brushes.DarkRed;
            LoginStatusText.Text = $"登录失败：{ex.Message}";
        }
        finally
        {
            LoginButton.IsEnabled = true;
        }
    }

    private void EnterExportPanel(SyncResponse sync)
    {
        UserInfoText.Text = $"已登录：{_client.UserName ?? _client.UserId.ToString()}"
                            + $"（共 {sync.UserConfig?.DiaryCount ?? sync.Diaries.Count} 篇日记）";
        LoginPanel.Visibility = Visibility.Collapsed;
        ExportPanel.Visibility = Visibility.Visible;
        FillDiaryList(sync.Diaries);
        Log($"同步完成：{sync.Diaries.Count} 篇日记，{sync.Images.Count} 张图片元数据");
    }

    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        _client.Logout();
        ExportPanel.Visibility = Visibility.Collapsed;
        LoginPanel.Visibility = Visibility.Visible;
        LoginStatusText.Text = "";
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择导出目录",
            FolderName = Directory.Exists(OutputDirBox.Text) ? OutputDirBox.Text : "",
        };
        if (dialog.ShowDialog(this) == true)
            OutputDirBox.Text = dialog.FolderName;
    }

    private void ScopeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;
        FromDatePicker.IsEnabled = ToDatePicker.IsEnabled = ScopeDateRadio.IsChecked == true;
        KeywordBox.IsEnabled = ScopeKeywordRadio.IsChecked == true;
        DiaryListBox.IsEnabled = ScopeSelectedRadio.IsChecked == true;
    }

    private async void RefreshListButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Log("正在刷新日记列表…");
            var sync = await _client.SyncAllAsync();
            FillDiaryList(sync.Diaries);
            Log($"列表已刷新：{sync.Diaries.Count} 篇");
        }
        catch (AuthExpiredException)
        {
            HandleAuthExpired();
        }
        catch (Exception ex)
        {
            Log($"刷新失败：{ex.Message}");
        }
    }

    private void FillDiaryList(List<DiaryEntry> diaries)
    {
        _diaryItems = diaries
            .OrderByDescending(d => d.CreatedTime)
            .Select(d => new DiaryListItem(d))
            .ToList();
        DiaryListBox.ItemsSource = null;
        DiaryListBox.ItemsSource = _diaryItems;
        DiaryListBox.SelectionMode = SelectionMode.Extended;
    }

    private void ToggleAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (DiaryListBox.SelectedItems.Count == _diaryItems.Count)
            DiaryListBox.UnselectAll();
        else
            DiaryListBox.SelectAll();
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        var outputDir = OutputDirBox.Text.Trim();
        if (string.IsNullOrEmpty(outputDir))
        {
            MessageBox.Show(this, "请选择输出目录", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (HtmlBox.IsChecked != true && MarkdownBox.IsChecked != true
            && PdfPerDiaryBox.IsChecked != true && MergedPdfBox.IsChecked != true)
        {
            MessageBox.Show(this, "请至少选择一种导出格式", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var options = new ExportOptions
        {
            OutputDir = outputDir,
            ExportHtml = HtmlBox.IsChecked == true,
            ExportMarkdown = MarkdownBox.IsChecked == true,
            ExportPdfPerDiary = PdfPerDiaryBox.IsChecked == true,
            ExportMergedPdf = MergedPdfBox.IsChecked == true,
        };

        if (ScopeDateRadio.IsChecked == true)
        {
            options.FromDate = FromDatePicker.SelectedDate is { } f ? DateOnly.FromDateTime(f) : null;
            options.ToDate = ToDatePicker.SelectedDate is { } t ? DateOnly.FromDateTime(t) : null;
            if (options.FromDate is { } fd && options.ToDate is { } td && fd > td)
            {
                MessageBox.Show(this, "开始日期不能晚于结束日期", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }
        else if (ScopeKeywordRadio.IsChecked == true)
        {
            options.Keyword = KeywordBox.Text.Trim();
        }
        else if (ScopeSelectedRadio.IsChecked == true)
        {
            var selected = DiaryListBox.SelectedItems.Cast<DiaryListItem>().Select(i => i.Diary.Id).ToHashSet();
            if (selected.Count == 0)
            {
                MessageBox.Show(this, "请先在列表中勾选要导出的日记", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            options.SelectedIds = selected;
        }

        SetExportingState(true);
        _exportCts = new CancellationTokenSource();
        var progress = new Progress<ExportProgress>(p =>
        {
            if (p.Total > 0)
                ExportProgress.Value = Math.Min(100, 100.0 * p.Done / p.Total);
            ProgressText.Text = p.Message;
            Log(p.Message);
        });

        try
        {
            var result = await _exportService.RunAsync(_client, options, progress, _exportCts.Token);
            _lastOutputDir = result.OutputDir;
            OpenFolderButton.IsEnabled = true;
            Log($"导出结束：成功 {result.Exported}，跳过 {result.Skipped}，失败 {result.Failures.Count} 项");
            foreach (var f in result.Failures)
                Log($"  [失败] {f.Stage}：日记 {f.DiaryId} {f.Title} — {f.Reason}");
        }
        catch (OperationCanceledException)
        {
            Log("已取消。已导出内容保留，下次可断点续传。");
        }
        catch (AuthExpiredException)
        {
            HandleAuthExpired();
        }
        catch (Exception ex)
        {
            Log($"导出失败：{ex.Message}");
        }
        finally
        {
            _exportCts.Dispose();
            _exportCts = null;
            SetExportingState(false);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => _exportCts?.Cancel();

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastOutputDir is { } dir && Directory.Exists(dir))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    }

    private void SetExportingState(bool exporting)
    {
        StartButton.IsEnabled = !exporting;
        CancelButton.IsEnabled = exporting;
        ExportProgress.Value = 0;
    }

    private void HandleAuthExpired()
    {
        MessageBox.Show(this, "登录态已失效，请重新登录", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
        LogoutButton_Click(this, new RoutedEventArgs());
    }

    private void Log(string message)
    {
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        LogBox.ScrollToEnd();
    }
}