using System.Windows;
using Eiri.Reimbursement.Desktop.ViewModels;

namespace Eiri.Reimbursement.Desktop;

public partial class MainWindow
{
    private async void DingTalkSubmissionInfo_OnClick(object sender, RoutedEventArgs e)
    {
        if (_dingTalkStore is null || _dingTalkDirectory is null || _submissionInfoStore is null
            || DataContext is not MainWindowViewModel vm || !vm.CanConnectDingTalk) return;
        vm.IsBusy = true;
        try
        {
            var connection = await _dingTalkStore.GetDingTalkConnectionAsync();
            if (connection is null)
            {
                MessageBox.Show(this, "请先通过“钉钉 → 连接接口”保存应用连接。", "报销提审信息", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            DingTalkSubmissionInfoViewModel editor = new(_dingTalkDirectory, _submissionInfoStore, connection.AccessToken, _dingTalkFormClient, _dingTalkFormStore);
            await editor.InitializeAsync();
            if (!string.IsNullOrEmpty(editor.StatusMessage))
            {
                MessageBox.Show(this, editor.StatusMessage, "报销提审信息", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            new DingTalkSubmissionInfoWindow(editor) { Owner = this }.ShowDialog();
        }
        catch (Exception) { vm.StatusMessage = "无法读取报销提审信息，请检查资料库访问权限。"; }
        finally { vm.IsBusy = false; }
    }

    private async void ConnectDingTalk_OnClick(object sender, RoutedEventArgs e)
    {
        if (_dingTalkClient is null || _dingTalkStore is null || DataContext is not MainWindowViewModel vm || !vm.CanConnectDingTalk) return;
        vm.IsBusy = true;
        try
        {
            var saved = await _dingTalkStore.GetDingTalkConnectionAsync();
            new DingTalkConnectionWindow(_dingTalkClient, _dingTalkStore, saved) { Owner = this }.ShowDialog();
        }
        catch (Exception) { vm.StatusMessage = "无法读取钉钉连接记录，请检查资料库访问权限。"; }
        finally { vm.IsBusy = false; }
    }

    private async void ClearDingTalk_OnClick(object sender, RoutedEventArgs e)
    {
        if (_dingTalkStore is null || DataContext is not MainWindowViewModel vm || !vm.CanConnectDingTalk) return;
        if (MessageBox.Show(this, "将清除本机保存的 Client ID、Client Secret、AccessToken 和报销提审信息，是否继续？",
            "清除连接记录", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        vm.IsBusy = true;
        try
        {
            await _dingTalkStore.ClearDingTalkConnectionAsync();
            vm.StatusMessage = "钉钉连接记录已清除。";
        }
        catch (Exception) { vm.StatusMessage = "清除连接记录失败，请检查资料库访问权限后重试。"; }
        finally { vm.IsBusy = false; }
    }
}
