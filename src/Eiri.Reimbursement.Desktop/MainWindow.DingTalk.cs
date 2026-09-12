using System.Windows;
using Eiri.Reimbursement.Desktop.ViewModels;

namespace Eiri.Reimbursement.Desktop;

public partial class MainWindow
{
    internal async Task OpenDingTalkApprovalAsync(ReimbursementEditorViewModel editor)
    {
        if (DataContext is not MainWindowViewModel vm || vm.IsBusy) return;
        if (_dingTalkStore is null || _dingTalkDirectory is null || _submissionInfoStore is null || _dingTalkFormClient is null
            || _dingTalkFormStore is null || _dingTalkInvoiceImages is null || _dingTalkImagePublisher is null || _dingTalkForecastClient is null
            || vm.ReimbursementWorkspace is not { } workspace)
        { editor.StatusMessage = "钉钉提审服务不可用，请重启完整安装的软件。"; return; }
        vm.IsBusy = true;
        string imageDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "EiriReimbursementHelper", "approval-images", Guid.NewGuid().ToString("N"));
        try
        {
            if (!await editor.PendingSave) { editor.StatusMessage = "请先修正并保存报销单字段。"; return; }
            await editor.PendingImport;
            var connection = await _dingTalkStore.GetDingTalkConnectionAsync();
            if (connection is null) { editor.StatusMessage = "请先通过“选项 → 设置 → 钉钉 → 连接接口”保存应用连接。"; return; }
            DingTalkApprovalViewModel approval = new(workspace, editor.Id, connection.AccessToken, _dingTalkFormClient,
                _dingTalkFormStore, _dingTalkDirectory, _submissionInfoStore, _dingTalkInvoiceImages, _dingTalkImagePublisher,
                _dingTalkForecastClient, imageDirectory, approvalClient: _dingTalkApprovalClient);
            DingTalkApprovalWindow dialog = new(approval) { Owner = this };
            dialog.ShowDialog();
            await dialog.PendingWork;
            await approval.WaitForUploadsAsync();
            await vm.RefreshAfterDingTalkApprovalAsync(editor);
        }
        catch (Exception) { editor.StatusMessage = "打开或刷新钉钉提审失败，请检查资料库访问权限后重试。"; }
        finally
        {
            vm.IsBusy = false;
            try { if (System.IO.Directory.Exists(imageDirectory)) System.IO.Directory.Delete(imageDirectory, true); }
            catch (System.IO.IOException) { editor.StatusMessage = "提审临时图片暂时无法清理，请关闭占用文件的程序。"; }
            catch (UnauthorizedAccessException) { editor.StatusMessage = "提审临时图片清理失败，请检查临时目录权限。"; }
        }
    }

    internal async Task<DingTalkSubmissionInfoViewModel?> CreateSubmissionInfoAsync(CancellationToken cancellationToken)
    {
        if (_dingTalkStore is null || _dingTalkDirectory is null || _submissionInfoStore is null) return null;
        var connection = await _dingTalkStore.GetDingTalkConnectionAsync(cancellationToken);
        if (connection is null) return null;
        DingTalkSubmissionInfoViewModel editor = new(_dingTalkDirectory, _submissionInfoStore, connection.AccessToken, _dingTalkFormClient, _dingTalkFormStore);
        await editor.InitializeAsync(cancellationToken);
        return editor;
    }

    private async void ConnectDingTalk_OnClick(object sender, RoutedEventArgs e) => await ConnectDingTalkAsync(this);

    internal async Task ConnectDingTalkAsync(Window owner)
    {
        if (_dingTalkClient is null || _dingTalkStore is null || DataContext is not MainWindowViewModel vm || !vm.CanConnectDingTalk) return;
        vm.IsBusy = true;
        try
        {
            bool reimport = false;
            do
            {
                bool success = await ConnectionState.ConnectAsync(token => ImportDingTalkCredentialsAsync(owner, token), reimport, _connectionLifetime.Token);
                if (_connectionLifetime.IsCancellationRequested || success || !ConnectionState.HasError) break;
                DingTalkConnectionWindow result = new(ConnectionState) { Owner = owner };
                result.ShowDialog();
                reimport = result.ReimportRequested;
            } while (reimport);
        }
        catch (Exception) { vm.StatusMessage = "无法读取钉钉连接记录，请检查资料库访问权限。"; }
        finally { vm.IsBusy = false; }
    }

    private async Task<Eiri.Reimbursement.Core.DingTalk.DingTalkCredentials?> ImportDingTalkCredentialsAsync(Window owner, CancellationToken cancellationToken)
    {
        Microsoft.Win32.OpenFileDialog picker = new() { Title = "导入钉钉 JSON 凭据", Filter = "JSON 凭据文件|*.json", CheckFileExists = true };
        if (picker.ShowDialog(owner) != true) return null;
        var file = new System.IO.FileInfo(picker.FileName);
        if (file.Length > 1024 * 1024) throw new InvalidOperationException("凭据 JSON 文件不能超过 1 MB。");
        string json = await System.IO.File.ReadAllTextAsync(picker.FileName, cancellationToken);
        return Eiri.Reimbursement.Core.DingTalk.DingTalkCredentials.Parse(json);
    }

    internal async Task<bool> ClearDingTalkAsync(Window owner)
    {
        if (_dingTalkStore is null || DataContext is not MainWindowViewModel vm || !vm.CanConnectDingTalk) return false;
        if (MessageBox.Show(owner, "将清除本机保存的 Client ID、Client Secret、AccessToken 和报销提审信息，是否继续？",
            "清除连接记录", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return false;
        vm.IsBusy = true;
        try
        {
            await _dingTalkStore.ClearDingTalkConnectionAsync();
            await ConnectionState.InitializeAsync(_connectionLifetime.Token);
            vm.StatusMessage = "钉钉连接记录已清除。";
            return true;
        }
        catch (Exception) { vm.StatusMessage = "清除连接记录失败，请检查资料库访问权限后重试。"; return false; }
        finally { vm.IsBusy = false; }
    }
}
