using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Eiri.Reimbursement.Desktop;

public partial class SettingsWindow : Window
{
    private readonly MainWindow _main;
    private readonly ExportPreferences _preferences;
    private CancellationTokenSource _lifetime = new();
    private Task _submissionLoad = Task.CompletedTask;
    private bool _cancelingLoad;
    private DingTalkSubmissionInfoView? _submission;
    private bool _busy;
    private bool _closeReady;

    public SettingsWindow(MainWindow main, ExportPreferences preferences)
    {
        _main = main;
        _preferences = preferences;
        InitializeComponent();
        ConnectionPanel.DataContext = main.ConnectionState;
        main.ConnectionState.PropertyChanged += Connection_OnChanged;
        UpdateCredential();
        try { ShowDirectory(); }
        catch (Exception) { SettingsStatus.Text = "读取导出设置失败，请重新选择文件夹保存设置。"; }
        Loaded += async (_, _) => await RunAsync(LoadSubmissionAsync);
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _lifetime.Cancel();
            _submission?.Cancel();
            main.ConnectionState.PropertyChanged -= Connection_OnChanged;
        };
    }

    private void Category_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DingTalkScroll is null || ExportPanel is null) return;
        DingTalkScroll.Visibility = Categories.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        ExportPanel.Visibility = Categories.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Connection_OnChanged(object? sender, PropertyChangedEventArgs e) => UpdateCredential();
    private void CredentialVisibility_OnChanged(object sender, RoutedEventArgs e) => UpdateCredential();
    private void UpdateCredential()
    {
        if (CredentialOutput is null) return;
        string token = _main.ConnectionState.AccessToken;
        CredentialOutput.Text = token.Length == 0 ? "尚无连接凭据" : ShowCredential.IsChecked == true ? token : "••••••••••••••••";
    }

    private Task LoadSubmissionAsync() => _submissionLoad = LoadSubmissionCoreAsync();

    private async Task LoadSubmissionCoreAsync()
    {
        _submission?.Cancel();
        _submission = null;
        SubmissionContent.Content = new TextBlock { Text = "正在读取默认提审信息…" };
        var editor = await _main.CreateSubmissionInfoAsync(_lifetime.Token);
        if (editor is null)
        {
            SubmissionContent.Content = new TextBlock { Text = "连接接口后，可设置默认提审信息。", TextWrapping = TextWrapping.Wrap };
            return;
        }
        _submission = new(editor);
        SubmissionContent.Content = _submission;
        if (editor.StatusMessage.Length == 0) await _submission.LoadAsync();
    }

    private async Task<bool> SaveSubmissionAsync()
    {
        if (_submission is null) return true;
        if (_submission.ViewModel.IsBusy)
        {
            SettingsStatus.Text = "正在读取或保存提审信息，请稍后重试。";
            return false;
        }
        if (await _submission.SaveAsync()) return true;
        Categories.SelectedIndex = 0;
        SettingsStatus.Text = "提审信息尚未保存，请修正后重试。";
        return false;
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy) return;
        _busy = true;
        SettingsBody.IsEnabled = false;
        SettingsStatus.Text = "正在处理…";
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception) { SettingsStatus.Text = "操作失败，请检查网络及资料库访问权限后重试。"; }
        finally
        {
            _busy = false;
            SettingsBody.IsEnabled = true;
            if (SettingsStatus.Text == "正在处理…") SettingsStatus.Text = "";
        }
    }

    private async void Connect_OnClick(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (!await SaveSubmissionAsync()) return;
        await _main.ConnectDingTalkAsync(this);
        await LoadSubmissionAsync();
    });

    private async void Clear_OnClick(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (!await SaveSubmissionAsync()) return;
        if (await _main.ClearDingTalkAsync(this))
        {
            await LoadSubmissionAsync();
            SettingsStatus.Text = "钉钉连接记录及默认提审信息已清除。";
        }
    });

    private void ShowDirectory() => ExportDirectoryOutput.Text = _preferences.LoadDirectory() ?? "未设置，每次导出时选择位置";

    private void ChooseDirectory_OnClick(object sender, RoutedEventArgs e)
    {
        OpenFolderDialog picker = new() { Title = "选择报销资料默认导出文件夹", Multiselect = false };
        if (picker.ShowDialog(this) == true) SaveDirectory(picker.FolderName);
    }

    private void ResetDirectory_OnClick(object sender, RoutedEventArgs e) => SaveDirectory(null);

    private void SaveDirectory(string? directory)
    {
        try
        {
            _preferences.SaveDirectory(directory);
            ShowDirectory();
            SettingsStatus.Text = directory is null ? "已清除默认导出位置。" : "默认导出位置已保存。";
        }
        catch (Exception) { SettingsStatus.Text = "保存导出设置失败，请检查文件夹是否存在及访问权限后重试。"; }
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeReady) return;
        e.Cancel = true;
        if (_cancelingLoad) return;
        if (!_submissionLoad.IsCompleted)
        {
            _cancelingLoad = true;
            _lifetime.Cancel();
            _submission?.Cancel();
            try { await _submissionLoad; }
            catch (OperationCanceledException) { }
            catch (Exception) { SettingsStatus.Text = "读取提审信息失败。"; }
            _lifetime.Dispose();
            _lifetime = new();
            _cancelingLoad = false;
            _ = Dispatcher.BeginInvoke(new Action(Close));
            return;
        }
        if (_busy) return;
        await RunAsync(async () =>
        {
            if (_submission is not null && !await _submission.SaveBeforeClosingAsync())
            {
                Categories.SelectedIndex = 0;
                SettingsStatus.Text = "提审信息尚未保存，请修正后重试。";
                return;
            }
            _closeReady = true;
            _ = Dispatcher.BeginInvoke(new Action(Close));
        });
    }
}
