using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
                var vm = new MainWindowViewModel(workspace, approvalStatusClient: new RenderStatusClient());
                vm.LoadAsync().GetAwaiter().GetResult();
                main = new MainWindow(vm);
                main.Show();
                main.UpdateLayout();
                AssertMaximizeRespectsWorkAreaAndRestores(main);
                var refresh = Assert.IsType<Button>(main.FindName("RefreshApprovalStatusesButton"));
                Assert.Same(vm.RefreshApprovalStatusesCommand, refresh.Command);
                var reimbursementGrid = (DataGrid)main.FindName("ReimbursementsGrid");
                var headers = reimbursementGrid.Columns.Select(c => c.Header?.ToString()).ToList();
                Assert.Equal("流程", headers[headers.IndexOf("已提交") + 1]);
                reimbursementGrid.SelectedItem = vm.Reimbursements[0];
                main.UpdateLayout();
                var mainDetail = (ReimbursementDetailView)main.FindName("ReimbursementDetailFields");
                var detailRefresh = Assert.IsType<Button>(mainDetail.FindName("RefreshApprovalStatusesButton"));
                Assert.Same(refresh.Command, detailRefresh.Command);
                ((TabControl)mainDetail.FindName("ReimbursementDetailTabs")).SelectedIndex = 1;
                main.UpdateLayout();
                var status = Assert.IsType<TextBox>(mainDetail.FindName("ApprovalStatusOutput"));
                Assert.True(status.IsReadOnly);
                var businessId = Assert.IsType<TextBox>(mainDetail.FindName("DingTalkBusinessIdOutput"));
                Assert.True(businessId.IsReadOnly);
                Assert.Empty(businessId.Text);
                Assert.Equal("未提交", status.Text);
                Assert.True(refresh.IsEnabled);
                Assert.True(detailRefresh.IsEnabled);
                status.BringIntoView();
                main.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                CaptureIfRequested(main, "approval-status-light");
                workspace.BeginApprovalSubmissionAsync(id).GetAwaiter().GetResult();
                workspace.CompleteApprovalSubmissionAsync(id, "preview-instance").GetAwaiter().GetResult();
                workspace.SaveApprovalStatusAsync(id, "preview-instance", "RUNNING", businessId: "202609110001").GetAwaiter().GetResult();
                vm.ReloadReimbursementsAsync().GetAwaiter().GetResult();
                ThemeManager.Toggle(application.Resources);
                main.Width = main.MinWidth;
                main.UpdateLayout();
                Assert.Equal("审批中", status.Text);
                Assert.Equal("202609110001", businessId.Text);
                reimbursementGrid.ScrollIntoView(vm.Reimbursements[0], reimbursementGrid.Columns[6]);
                status.BringIntoView();
                main.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                CaptureIfRequested(main, "approval-status-dark-narrow");
                vm.IsBusy = true;
                main.UpdateLayout();
                Assert.False(refresh.IsEnabled);
                Assert.False(detailRefresh.IsEnabled);
                vm.IsBusy = false;
                ThemeManager.ApplyLightTheme(application.Resources);
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
                Assert.False(((Button)archive.FindName("RefreshApprovalStatusesButton")).IsVisible);
                Assert.False(((Button)detail.FindName("RefreshApprovalStatusesButton")).IsVisible);
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

    private sealed class RenderStatusClient : Eiri.Reimbursement.Core.DingTalk.IDingTalkApprovalStatusClient
    {
        public Task<Eiri.Reimbursement.Core.DingTalk.DingTalkApprovalState> GetInstanceStatusAsync(string accessToken, string instanceId, CancellationToken cancellationToken = default) => Task.FromResult(new Eiri.Reimbursement.Core.DingTalk.DingTalkApprovalState("RUNNING"));
    }

    private static void CaptureIfRequested(Window window, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("EIRI_UI_CAPTURE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        window.UpdateLayout();
        RenderTargetBitmap bitmap = new((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(file);
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
