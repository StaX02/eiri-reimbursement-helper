using System.Windows;
using System.Windows.Controls;
using Eiri.Reimbursement.Desktop.ViewModels;

namespace Eiri.Reimbursement.Desktop;

public partial class MainWindow
{
    private void ConfigureArchiveWindow()
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        SourceInitialized += (_, _) => PositionArchiveWindow();
        Title = "归档 · 发票报销助手";
        WindowTitle.Text = "归档";
        WindowSubtitle.Text = "只读查看";
        ManagementHeading.Text = "归档";
        OrdersGrid.ContextMenu = null;
        var restore = new MenuItem { Header = "取消归档" };
        restore.Click += UnarchiveReimbursements_OnClick;
        var menu = new ContextMenu();
        menu.Items.Add(restore);
        menu.Opened += (_, _) => restore.IsEnabled =
            DataContext is MainWindowViewModel { IsBusy: false } && ReimbursementsGrid.SelectedItems.Count > 0;
        ReimbursementsGrid.ContextMenu = menu;
    }

    private async void ViewArchive_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel { CanOpenArchive: true } vm) return;
        try
        {
            await vm.FlushReimbursementChangesAsync();
            var archive = vm.CreateArchiveViewModel();
            await archive.LoadAsync();
            var window = new MainWindow(archive) { Owner = this };
            window.ShowDialog();
            await vm.LoadAsync();
        }
        catch (Exception exception) { vm.StatusMessage = $"打开归档失败：{exception.Message}"; }
    }

    private async void UnarchiveReimbursements_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel { IsArchive: true } vm) return;
        await vm.UnarchiveReimbursementsAsync(GetSelectedReimbursementIds());
        if (Owner?.DataContext is MainWindowViewModel owner) await owner.LoadAsync();
    }
}
