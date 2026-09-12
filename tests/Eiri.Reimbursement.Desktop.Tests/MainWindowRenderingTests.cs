using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Eiri.Reimbursement.Core.Orders;
using Eiri.Reimbursement.Core.DingTalk;
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
                var settingsClient = new SettingsClient();
                main = new MainWindow(vm, dingTalkClient: settingsClient, dingTalkStore: workspace, dingTalkDirectory: settingsClient,
                    submissionInfoStore: workspace, dingTalkFormClient: settingsClient, dingTalkFormStore: workspace);
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
                Assert.Equal("归档", ((MenuItem)menu.Items[0]).Header);
                Assert.Null(main.FindName("DingTalkMenu"));
                Assert.Equal("设置", ((MenuItem)main.FindName("SettingsMenuItem")).Header);
                VerifySettings(main, root, application, workspace, settingsClient);
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

    private static void VerifySettings(MainWindow main, string root, App application, SqliteReimbursementWorkspace workspace, SettingsClient client)
    {
        var preferences = new ExportPreferences(Path.Combine(root, "export-settings.json"));
        preferences.SaveDirectory(root);
        var settings = new SettingsWindow(main, preferences) { Owner = main };
        try
        {
            settings.Show();
            settings.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            settings.UpdateLayout();
            var categories = (ListBox)settings.FindName("Categories");
            var dingTalk = (Grid)settings.FindName("DingTalkPanel");
            var export = (ScrollViewer)settings.FindName("ExportPanel");
            Assert.True(dingTalk.IsVisible);
            Assert.False(export.IsVisible);
            Assert.True(dingTalk.ActualWidth > categories.ActualWidth);
            Assert.Equal("尚无连接凭据", ((TextBox)settings.FindName("CredentialOutput")).Text);
            CaptureIfRequested(settings, "settings-dingtalk-light");
            categories.SelectedIndex = 1;
            settings.UpdateLayout();
            Assert.False(dingTalk.IsVisible);
            Assert.True(export.IsVisible);
            Assert.Equal(root, ((TextBox)settings.FindName("ExportDirectoryOutput")).Text);
            CaptureIfRequested(settings, "settings-export-light");
            ThemeManager.Toggle(application.Resources);
            settings.Width = settings.MinWidth;
            settings.Height = settings.MinHeight;
            categories.SelectedIndex = 0;
            settings.UpdateLayout();
            Assert.True(dingTalk.ActualWidth > categories.ActualWidth);
            CaptureIfRequested(settings, "settings-dingtalk-dark-narrow");
            categories.SelectedIndex = 1;
            settings.UpdateLayout();
            CaptureIfRequested(settings, "settings-export-dark-narrow");
        }
        finally
        {
            settings.Close();
            settings.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            ThemeManager.ApplyLightTheme(application.Resources);
        }
        Assert.False(settings.IsVisible);
        workspace.SaveDingTalkConnectionAsync(new("test-client", "test-secret", "test-token", DateTimeOffset.UtcNow.AddHours(1))).GetAwaiter().GetResult();
        main.ConnectionState.InitializeAsync().GetAwaiter().GetResult();
        settings = new SettingsWindow(main, preferences) { Owner = main };
        try
        {
            settings.Show();
            settings.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var editor = Assert.IsType<DingTalkSubmissionInfoView>(((ContentControl)settings.FindName("SubmissionContent")).Content);
            Assert.Equal(8, editor.ViewModel.FormFields.Count);
            Assert.DoesNotContain("test-token", ((TextBox)settings.FindName("CredentialOutput")).Text);
            editor.ViewModel.FormFields[0].Text = "测试默认值";
            var categories = (ListBox)settings.FindName("Categories");
            categories.SelectedIndex = 1;
            categories.SelectedIndex = 0;
            Assert.Equal("测试默认值", editor.ViewModel.FormFields[0].Text);
            settings.UpdateLayout();
            CaptureIfRequested(settings, "settings-connected-light");
            settings.Width = settings.MinWidth;
            settings.Height = settings.MinHeight;
            ThemeManager.Toggle(application.Resources);
            settings.UpdateLayout();
            var scroll = (ScrollViewer)editor.FindName("PrefillScroll");
            Assert.True(scroll.ScrollableHeight > 0);
            Assert.True(scroll.ViewportHeight > 100);
            ((ScrollViewer)settings.FindName("DingTalkScroll")).ScrollToEnd();
            scroll.ScrollToEnd();
            settings.UpdateLayout();
            CaptureIfRequested(settings, "settings-connected-dark-narrow");
        }
        finally
        {
            settings.Close();
            settings.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            ThemeManager.ApplyLightTheme(application.Resources);
        }
        Assert.False(settings.IsVisible);
        Assert.Equal("测试默认值", workspace.GetFormPrefillAsync("test-template").GetAwaiter().GetResult()["field-0"].Values[0]);
        foreach (bool reconnect in new[] { false, true })
        {
            client.PauseTemplate = !reconnect;
            client.CancellationObserved = false;
            settings = new SettingsWindow(main, preferences) { Owner = main };
            settings.Show();
            settings.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            if (reconnect)
            {
                client.PauseTemplate = true;
                ((Button)settings.FindName("ConnectButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            settings.Close();
            for (int i = 0; i < 20 && settings.IsVisible; i++)
                settings.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert.True(client.CancellationObserved);
            Assert.False(settings.IsVisible);
        }
    }

    private sealed class SettingsClient : IDingTalkDirectoryClient, IDingTalkFormClient, IDingTalkAccessTokenClient
    {
        public Task<DingTalkAccessToken> GetAccessTokenAsync(string clientId, string clientSecret, CancellationToken cancellationToken = default)
            => Task.FromResult(new DingTalkAccessToken("test-refreshed-token", 3600));
        public bool PauseTemplate;
        public bool CancellationObserved;
        public Task<IReadOnlyList<DingTalkDepartment>> GetDepartmentsAsync(string accessToken, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DingTalkDepartment>>([]);
        public Task<IReadOnlyList<DingTalkUser>> GetUsersAsync(string accessToken, long deptId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DingTalkUser>>([]);
        public Task<DingTalkFormTemplate> GetReimbursementTemplateAsync(string accessToken, CancellationToken cancellationToken = default)
        {
            if (PauseTemplate)
            {
                var pending = new TaskCompletionSource<DingTalkFormTemplate>();
                cancellationToken.Register(() => { CancellationObserved = true; pending.TrySetCanceled(cancellationToken); });
                return pending.Task;
            }
            return Task.FromResult(new DingTalkFormTemplate("test-template", Enumerable.Range(0, 8)
                .Select(i => new DingTalkFormField($"field-{i}", $"测试字段 {i + 1}", "TextField", [])).ToArray(), null));
        }
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
