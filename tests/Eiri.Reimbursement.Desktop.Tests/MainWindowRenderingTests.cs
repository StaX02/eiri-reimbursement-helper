using System.IO;
using System.Windows;
using System.Windows.Controls;
using Eiri.Reimbursement.Core.Orders;
using Eiri.Reimbursement.Core.Reimbursements;
using Eiri.Reimbursement.Desktop;
using Eiri.Reimbursement.Desktop.ViewModels;
using Eiri.Reimbursement.Infrastructure.Sqlite;

namespace Eiri.Reimbursement.Desktop.Tests;

public sealed class MainWindowRenderingTests
{
    [Fact]
    public void WindowsLoadWithSelectionAndReadOnlyArchiveBindings()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            string root = Path.Combine(Path.GetTempPath(), "eiri-window-smoke", Guid.NewGuid().ToString("N"));
            MainWindow? main = null;
            MainWindow? archive = null;
            try
            {
                App application = new(startWorkspace: false);
                application.InitializeComponent();
                var workspace = new SqliteReimbursementWorkspace(root);
                workspace.InitializeAsync().GetAwaiter().GetResult();
                var a = workspace.CreateOrderAsync(new(OrderPlatform.JD)).GetAwaiter().GetResult();
                workspace.CreateOrderAsync(new(OrderPlatform.Taobao)).GetAwaiter().GetResult();
                var id = workspace.CreateReimbursementAsync([a]).GetAwaiter().GetResult();
                var vm = new MainWindowViewModel(workspace);
                vm.LoadAsync().GetAwaiter().GetResult();
                main = new MainWindow(vm);
                main.Show();
                main.UpdateLayout();
                var menu = (Menu)main.FindName("TopMenuBar");
                Assert.Equal("归档", ((MenuItem)menu.Items[1]).Header);
                var orders = (DataGrid)main.FindName("OrdersGrid");
                orders.SelectedItem = vm.Orders[0];
                main.UpdateLayout();
                var tabs = (TabControl)main.FindName("OrderDetailTabs");
                Assert.True(tabs.IsVisible);
                orders.SelectAll();
                main.UpdateLayout();
                Assert.False(tabs.IsVisible);

                workspace.SetReimbursementMilestonesAsync(Enum.GetValues<Milestone>()
                    .Select(m => new SetReimbursementMilestoneCommand(id, m, DateTimeOffset.UtcNow)).ToArray())
                    .GetAwaiter().GetResult();
                var archiveVm = vm.CreateArchiveViewModel();
                archiveVm.LoadAsync().GetAwaiter().GetResult();
                archive = new MainWindow(archiveVm) { Owner = main };
                archive.Show();
                var forms = (DataGrid)archive.FindName("ReimbursementsGrid");
                forms.SelectedItem = Assert.Single(archiveVm.Reimbursements);
                archive.UpdateLayout();
                var detail = (ReimbursementDetailView)archive.FindName("ReimbursementDetailFields");
                ((TabControl)detail.FindName("ReimbursementDetailTabs")).SelectedIndex = 1;
                archive.UpdateLayout();
                Assert.True(((TextBox)detail.FindName("ContentInput")).IsReadOnly);
                Assert.False(((FrameworkElement)detail.FindName("ReimbursementDropZone")).IsVisible);
                Assert.False(((Button)archive.FindName("CreateOrderButton")).IsVisible);
                Assert.Equal("取消归档", ((MenuItem)Assert.Single(forms.ContextMenu!.Items.Cast<object>())).Header);
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                archive?.Close();
                main?.Close();
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Window smoke test did not finish.");
        Assert.Null(failure);
    }
}
