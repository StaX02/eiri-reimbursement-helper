using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
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
                AssertMaximizeRespectsWorkAreaAndRestores(main);
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
                AssertMaximizeRespectsWorkAreaAndRestores(archive);
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

    private static void AssertMaximizeRespectsWorkAreaAndRestores(Window window)
    {
        window.UpdateLayout();
        var originalSize = new Size(window.ActualWidth, window.ActualHeight);
        var originalOrigin = window.PointToScreen(new Point());
        var monitor = MonitorFromWindow(new WindowInteropHelper(window).Handle, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        Assert.True(GetMonitorInfo(monitor, ref info));
        var expectedTopLeft = new Point(info.WorkLeft, info.WorkTop);
        var expectedBottomRight = new Point(info.WorkRight, info.WorkBottom);
        window.WindowState = WindowState.Maximized;
        window.UpdateLayout();
        var topLeft = window.PointToScreen(new Point());
        var bottomRight = window.PointToScreen(new Point(window.ActualWidth, window.ActualHeight));
        Assert.True(Math.Abs(bottomRight.Y - expectedBottomRight.Y) <= 1,
            $"Maximized content bottom {bottomRight.Y} must meet work-area bottom {expectedBottomRight.Y}.");
        Assert.InRange(Math.Abs(topLeft.X - expectedTopLeft.X), 0, 1);
        Assert.InRange(Math.Abs(topLeft.Y - expectedTopLeft.Y), 0, 1);
        Assert.InRange(Math.Abs(bottomRight.X - expectedBottomRight.X), 0, 1);
        window.WindowState = WindowState.Normal;
        window.UpdateLayout();
        Assert.Equal(originalSize, new Size(window.ActualWidth, window.ActualHeight));
        Assert.Equal(originalOrigin, window.PointToScreen(new Point()));
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public int MonitorLeft, MonitorTop, MonitorRight, MonitorBottom;
        public int WorkLeft, WorkTop, WorkRight, WorkBottom;
        public uint Flags;
    }
}
