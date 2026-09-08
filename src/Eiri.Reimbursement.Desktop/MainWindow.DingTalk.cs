using System.Windows;
using Eiri.Reimbursement.Desktop.ViewModels;

namespace Eiri.Reimbursement.Desktop;

public partial class MainWindow
{
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
        if (MessageBox.Show(this, "将清除本机保存的 Client ID、Client Secret 和 AccessToken，是否继续？",
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
