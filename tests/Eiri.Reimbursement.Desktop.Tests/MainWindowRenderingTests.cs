using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Eiri.Reimbursement.Core.Orders;
using Eiri.Reimbursement.Desktop;
using Eiri.Reimbursement.Desktop.ViewModels;
using Eiri.Reimbursement.Infrastructure.Sqlite;
using Eiri.Reimbursement.Core.DingTalk;

namespace Eiri.Reimbursement.Desktop.Tests;

public sealed class MainWindowRenderingTests
{
    [Fact]
    public void MainWindowLoadsAndRestrictsDetailsToSingleSelection()
    {
        Exception? renderingException = null;
        Thread uiThread = new(() =>
        {
            try
            {
                // Modal tests pump startup events; use only the isolated test workspace.
                App application = new(startWorkspace: false);
                application.InitializeComponent();
                string libraryRoot = Path.Combine(Path.GetTempPath(), "eiri-sidebar-render", Guid.NewGuid().ToString("N"));
                SqliteReimbursementWorkspace workspace = new(libraryRoot);
                workspace.InitializeAsync().GetAwaiter().GetResult();
                MainWindowViewModel viewModel = new(workspace);
                MainWindow window = new(viewModel, new TestTokenClient(), workspace, new TestDirectoryClient(), workspace, new TestFormClient(), workspace);
                window.Show();
                window.UpdateLayout();
                AssertApprovalWindow(application, window);
                Menu topMenu = Assert.IsType<Menu>(window.FindName("TopMenuBar"));
                Assert.Equal("钉钉", Assert.IsType<MenuItem>(topMenu.Items[0]).Header);
                Assert.Equal("归档", Assert.IsType<MenuItem>(topMenu.Items[1]).Header);
                Assert.Equal("查看归档", Assert.IsType<MenuItem>(Assert.IsType<MenuItem>(topMenu.Items[1]).Items[0]).Header);
                Assert.Equal("选项", Assert.IsType<MenuItem>(topMenu.Items[2]).Header);
                window.ConnectionState.ConnectAsync(_ => Task.FromResult<DingTalkCredentials?>(new("test-client", "test-secret"))).GetAwaiter().GetResult();
                DingTalkConnectionWindow connectionWindow = new(window.ConnectionState) { Owner = window };
                connectionWindow.Show();
                Assert.Equal(TestTokenClient.Token, Assert.IsType<TextBox>(connectionWindow.FindName("AccessTokenOutput")).Text);
                Assert.Equal(TestTokenClient.Token, workspace.GetDingTalkConnectionAsync().GetAwaiter().GetResult()!.AccessToken);
                SavePreview(connectionWindow, "dingtalk-connection.png");
                ThemeManager.Toggle(application.Resources);
                SavePreview(connectionWindow, "dingtalk-connection-dark.png");
                ThemeManager.Toggle(application.Resources);
                connectionWindow.Close();
                var connectionStatus = Assert.IsType<Button>(window.FindName("DingTalkConnectionStatusButton"));
                var connectionLabel = Assert.IsType<TextBlock>(connectionStatus.Content);
                Assert.Equal("钉钉：连接成功", connectionLabel.Text);
                var connectedColor = ((System.Windows.Media.SolidColorBrush)connectionLabel.Foreground).Color;
                window.Width = 980; window.UpdateLayout();
                SavePreview(window, "dingtalk-status-narrow.png");
                window.Width = 1240;
                var savedConnection = workspace.GetDingTalkConnectionAsync().GetAwaiter().GetResult()!;
                workspace.SaveDingTalkConnectionAsync(savedConnection with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) }).GetAwaiter().GetResult();
                window.ConnectionState.InitializeAsync().GetAwaiter().GetResult(); window.UpdateLayout();
                Assert.Equal("钉钉：连接过期", connectionLabel.Text);
                Assert.NotEqual(connectedColor, ((System.Windows.Media.SolidColorBrush)connectionLabel.Foreground).Color);
                SavePreview(window, "dingtalk-status-expired.png");
                window.ConnectionState.ConnectAsync(_ => throw new InvalidOperationException("测试连接错误"), true).GetAwaiter().GetResult();
                DingTalkConnectionWindow failureWindow = new(window.ConnectionState) { Owner = window };
                failureWindow.Show(); failureWindow.UpdateLayout();
                var reimport = Assert.IsType<Button>(failureWindow.FindName("ReimportButton"));
                Assert.True(reimport.IsVisible); SavePreview(failureWindow, "dingtalk-reimport-error.png");
                reimport.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.True(failureWindow.ReimportRequested);
                bool showedSuccessDialog = false;
                window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    foreach (var popup in application.Windows.OfType<DingTalkConnectionWindow>().ToArray())
                    {
                        showedSuccessDialog = true; popup.Close();
                    }
                }));
                connectionStatus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                Assert.Equal(DingTalkConnectionState.Connected, window.ConnectionState.State);
                Assert.False(showedSuccessDialog);
                Assert.DoesNotContain(Descendants<Button>(window), b => b.Content?.ToString() == "刷新");
                DingTalkSubmissionInfoViewModel submissionVm = new(new TestDirectoryClient(), workspace, "test-token", new TestFormClient(), workspace);
                submissionVm.InitializeAsync().GetAwaiter().GetResult();
                DingTalkSubmissionInfoWindow submissionWindow = new(submissionVm) { Owner = window };
                submissionWindow.Show();
                submissionWindow.UpdateLayout();
                Assert.NotEmpty(submissionVm.FormFields);
                Assert.DoesNotContain(submissionVm.FormFields, f => f.Definition.Label.Trim() is "报销类型" or "报销内容");
                var textPrefill = submissionVm.FormFields.First(f => f.IsText);
                textPrefill.Text = "测试预填内容";
                Assert.True(submissionVm.PendingFormSave.GetAwaiter().GetResult());
                Assert.Equal("测试预填内容", workspace.GetFormPrefillAsync(submissionVm.ProcessCode!).GetAwaiter().GetResult()[textPrefill.Definition.Id].Values.Single());
                ComboBox departmentInput = Assert.IsType<ComboBox>(submissionWindow.FindName("DepartmentInput"));
                ComboBox userInput = Assert.IsType<ComboBox>(submissionWindow.FindName("UserInput"));
                Assert.Null(departmentInput.SelectedItem);
                Assert.False(userInput.IsEnabled);
                SavePreview(submissionWindow, "dingtalk-submission-empty.png");
                departmentInput.IsDropDownOpen = true;
                Assert.Equal(2, departmentInput.Items.Count);
                departmentInput.SelectedIndex = 0;
                departmentInput.IsDropDownOpen = false;
                Assert.Equal(2, submissionVm.SelectedDepartment!.DeptId);
                Assert.True(userInput.IsEnabled);
                userInput.IsDropDownOpen = true;
                Assert.Single(userInput.Items);
                userInput.SelectedIndex = 0;
                userInput.IsDropDownOpen = false;
                Assert.Equal("first", workspace.GetSubmissionInfoAsync().GetAwaiter().GetResult().User!.UserId);
                SavePreview(submissionWindow, "dingtalk-submission-selected.png");
                ThemeManager.Toggle(application.Resources);
                SavePreview(submissionWindow, "dingtalk-submission-dark.png");
                var prefillScroll = Assert.IsType<ScrollViewer>(submissionWindow.FindName("PrefillScroll"));
                prefillScroll.ScrollToBottom();
                SavePreview(submissionWindow, "dingtalk-schema-bottom-dark.png");
                ThemeManager.Toggle(application.Resources);
                submissionWindow.Width = 400;
                submissionWindow.Height = 440;
                SavePreview(submissionWindow, "dingtalk-schema-narrow.png");
                departmentInput.SelectedIndex = 1;
                Assert.Null(submissionVm.SelectedUser);
                Assert.Null(userInput.SelectedItem);
                submissionWindow.Close();
                viewModel.Orders =
                [
                    new OrderListItem(
                        OrderId.New(),
                        OrderPlatform.Other,
                        null,
                        ["商家甲", "商家乙"],
                        [],
                        0,
                        ["1001", "10000002"],
                        2,
                        DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow),
                ];
                window.RefreshOrdersList();
                window.UpdateLayout();
                FrameworkElement detailPanel = Assert.IsAssignableFrom<FrameworkElement>(
                    window.FindName("OrderDetailPanel"));
                FrameworkElement detailActions = Assert.IsAssignableFrom<FrameworkElement>(
                    window.FindName("OrderDetailActions"));
                FrameworkElement materialDropZones = Assert.IsAssignableFrom<FrameworkElement>(
                    window.FindName("MaterialDropZones"));
                TabControl detailTabs = Assert.IsType<TabControl>(window.FindName("OrderDetailTabs"));
                DataGrid ordersGrid = Assert.IsType<DataGrid>(window.FindName("OrdersGrid"));
                Assert.Single(ordersGrid.Items);
                Assert.Equal(DataGridSelectionMode.Extended, ordersGrid.SelectionMode);
                Assert.Equal(Visibility.Collapsed, detailPanel.Visibility);

                OrderListItem selectedOrder = new(
                    OrderId.New(),
                    OrderPlatform.Taobao,
                    null,
                    [],
                    [],
                    0,
                    [],
                    0,
                    null,
                    null,
                    null,
                    DateTimeOffset.UtcNow);
                viewModel.SelectedOrder = selectedOrder;
                window.UpdateLayout();

                Assert.Equal(Visibility.Visible, detailPanel.Visibility);

                Assert.Equal(Visibility.Visible, detailActions.Visibility);
                viewModel.SetSelectedOrders(
                    [selectedOrder, selectedOrder with { Id = OrderId.New() }]);
                window.UpdateLayout();

                Assert.Equal(Visibility.Collapsed, detailActions.Visibility);
                Assert.Equal(Visibility.Collapsed, materialDropZones.Visibility);
                Assert.Equal(Visibility.Collapsed, detailTabs.Visibility);

                BatchInvoiceImportWindow batchWindow = new(viewModel);
                Border batchDropZone = Assert.IsType<Border>(
                    batchWindow.FindName("BatchInvoiceDropZone"));
                Assert.True(batchDropZone.AllowDrop);
                batchWindow.Close();
                var firstOrder = workspace.CreateOrderAsync(new(OrderPlatform.JD)).GetAwaiter().GetResult();
                var secondOrder = workspace.CreateOrderAsync(new(OrderPlatform.Taobao)).GetAwaiter().GetResult();
                Guid firstId = workspace.CreateReimbursementAsync([firstOrder]).GetAwaiter().GetResult();
                Guid secondId = workspace.CreateReimbursementAsync([secondOrder]).GetAwaiter().GetResult();
                workspace.UpdateReimbursementAsync(new(firstId, new DateOnly(2026, 8, 25), "材料费", "IGCT驱动芯片测试PCB", 507874)).GetAwaiter().GetResult();
                viewModel.LoadAsync().GetAwaiter().GetResult();
                DataGrid reimbursementGrid = Assert.IsType<DataGrid>(window.FindName("ReimbursementsGrid"));
                Assert.Equal(new[] { "申请日期", "报销类型", "报销内容", "总金额", "已导出", "已提交", "已返款" }, reimbursementGrid.Columns.Select(c => c.Header.ToString()));
                Assert.Equal(DataGridSelectionMode.Extended, reimbursementGrid.SelectionMode);
                Assert.True(reimbursementGrid.TranslatePoint(new Point(0, 0), window).Y > ordersGrid.TranslatePoint(new Point(0, 0), window).Y);
                Border reimbursementPanel = Assert.IsType<Border>(window.FindName("ReimbursementDetailPanel"));
                var fields = Assert.IsType<ReimbursementDetailView>(window.FindName("ReimbursementDetailFields"));
                var firstRow = viewModel.Reimbursements.Single(row => row.Id == firstId);
                var secondRow = viewModel.Reimbursements.Single(row => row.Id == secondId);
                ordersGrid.UnselectAll();
                window.UpdateLayout();
                Assert.Equal(Visibility.Collapsed, reimbursementPanel.Visibility);
                reimbursementGrid.SelectedItems.Add(firstRow);
                window.UpdateLayout();
                Assert.Equal(Visibility.Visible, reimbursementPanel.Visibility);
                Assert.Equal(Visibility.Collapsed, detailPanel.Visibility);
                Assert.Empty(ordersGrid.SelectedItems);
                var reimbursementTabs = Assert.IsType<TabControl>(fields.FindName("ReimbursementDetailTabs"));
                Assert.Equal(new[] { "附件材料", "报销单属性", "关联订单", "提交/返款状态" }, reimbursementTabs.Items.Cast<TabItem>().Select(tab => tab.Header.ToString()));
                Assert.Same(detailTabs.Style, reimbursementTabs.Style);
                Assert.Same(ordersGrid.CellStyle, reimbursementGrid.CellStyle);
                Assert.Same(ordersGrid.ColumnHeaderStyle, reimbursementGrid.ColumnHeaderStyle);
                Assert.Equal(ordersGrid.GridLinesVisibility, reimbursementGrid.GridLinesVisibility);
                var reimbursementDrop = Assert.IsType<Border>(fields.FindName("ReimbursementDropZone"));
                var invoiceDrop = Assert.IsType<Border>(window.FindName("InvoiceDropZone"));
                Assert.True(reimbursementDrop.AllowDrop);
                Assert.Equal(invoiceDrop.Background, reimbursementDrop.Background);
                Assert.Equal(invoiceDrop.BorderBrush, reimbursementDrop.BorderBrush);
                Assert.True(reimbursementDrop.TranslatePoint(new Point(0, 0), window).Y < reimbursementTabs.TranslatePoint(new Point(0, 0), window).Y);
                reimbursementGrid.ScrollIntoView(firstRow);
                window.UpdateLayout();
                string[] incompleteStatuses = ["未导出", "未提交", "未返款"];
                for (int index = 0; index < incompleteStatuses.Length; index++)
                {
                    Assert.Equal(incompleteStatuses[index], Assert.IsType<TextBlock>(reimbursementGrid.Columns[index + 4].GetCellContent(firstRow)).Text);
                }
                var originalForm = firstRow.Form;
                var milestoneDate = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.FromHours(8));
                firstRow.Form = originalForm with { ExportedAt = milestoneDate, SubmittedAt = milestoneDate, RefundedAt = milestoneDate };
                window.UpdateLayout();
                for (int index = 4; index < 7; index++)
                {
                    Assert.Equal("2026-09-08", Assert.IsType<TextBlock>(reimbursementGrid.Columns[index].GetCellContent(firstRow)).Text);
                }
                firstRow.Form = originalForm;
                window.UpdateLayout();
                SavePreview(window, "reimbursement-tabs-attachments.png");
                reimbursementTabs.SelectedIndex = 3;
                window.UpdateLayout();
                SavePreview(window, "reimbursement-status.png");
                reimbursementTabs.SelectedIndex = 2;
                window.UpdateLayout();
                SavePreview(window, "reimbursement-tabs-orders.png");
                reimbursementTabs.SelectedIndex = 1;
                window.UpdateLayout();
                Assert.Equal("5078.74", Assert.IsType<TextBox>(fields.FindName("AmountInput")).Text);
                var contentInput = Assert.IsType<TextBox>(fields.FindName("ContentInput"));
                contentInput.Text = "即时保存的报销内容";
                Assert.True(viewModel.ReimbursementEditor!.PendingSave.GetAwaiter().GetResult());
                Assert.Same(firstRow, reimbursementGrid.SelectedItem);
                Assert.Equal("即时保存的报销内容", workspace.GetReimbursementAsync(firstId).GetAwaiter().GetResult()!.Form.Content);
                Assert.Equal("即时保存的报销内容", firstRow.ContentDisplay);
                Assert.Equal("即时保存的报销内容", viewModel.Orders.Single(order => order.Id == firstOrder).ReimbursementDisplay);
                SavePreview(window, "reimbursement-sidebar.png");
                window.Width = 980;
                window.Height = 660;
                window.UpdateLayout();
                Assert.True(reimbursementGrid.ActualHeight >= 80);
                SavePreview(window, "reimbursement-sidebar-narrow.png");
                ThemeManager.Toggle(application.Resources);
                window.UpdateLayout();
                SavePreview(window, "reimbursement-sidebar-dark.png");
                reimbursementTabs.SelectedIndex = 0;
                viewModel.ReimbursementEditor!.Attachments =
                [
                    new(Guid.NewGuid(), "报销单附件名称较长时应在卡片中省略并可通过提示查看完整名称.pdf", "sample.pdf", "首页部分字段未识别，请检查报销单文件并在报销单属性中补充申请日期、报销类型、报销内容和总金额。"),
                ];
                window.UpdateLayout();
                SavePreview(window, "reimbursement-tabs-attachments-dark.png");
                ThemeManager.Toggle(application.Resources);
                reimbursementGrid.SelectedItems.Add(secondRow);
                window.UpdateLayout();
                Assert.Equal("已选中多个报销单", Assert.IsType<TextBlock>(window.FindName("ReimbursementDetailHeading")).Text);
                Assert.False(fields.IsVisible);
                SavePreview(window, "reimbursement-sidebar-multiple.png");
                reimbursementGrid.UnselectAll();
                window.UpdateLayout();
                Assert.Equal(Visibility.Collapsed, reimbursementPanel.Visibility);
                Assert.Equal(Visibility.Collapsed, detailPanel.Visibility);
                reimbursementGrid.SelectedItems.Add(firstRow);
                ordersGrid.SelectedItems.Add(viewModel.Orders[0]);
                window.UpdateLayout();
                Assert.Empty(reimbursementGrid.SelectedItems);
                Assert.Equal(Visibility.Collapsed, reimbursementPanel.Visibility);
                Assert.Equal(Visibility.Visible, detailPanel.Visibility);
                VerifyPendingSubmissionClose(window, workspace, failFirstSave: false);
                VerifyPendingSubmissionClose(window, workspace, failFirstSave: true);
                VerifySubmissionModalSaveAndClose(window, viewModel);
                VerifyArchiveWindow(window, workspace, application);
                window.Close();
                Directory.Delete(libraryRoot, recursive: true);
            }
            catch (Exception exception)
            {
                renderingException = exception;
            }
        });
        uiThread.SetApartmentState(ApartmentState.STA);

        uiThread.Start();

        Assert.True(uiThread.Join(TimeSpan.FromSeconds(60)), "UI rendering did not complete in time.");
        Assert.Null(renderingException);
    }
    private static void SavePreview(Window window, string name)
    {
        string? output = Environment.GetEnvironmentVariable("EIRI_QA_OUTPUT");
        if (output is null) return;
        Directory.CreateDirectory(output);
        window.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output, name));
        encoder.Save(file);
    }

    private static void VerifyArchiveWindow(MainWindow owner, SqliteReimbursementWorkspace workspace, App application)
    {
        var order = workspace.CreateOrderAsync(new(OrderPlatform.JD)).GetAwaiter().GetResult();
        var id = workspace.CreateReimbursementAsync([order]).GetAwaiter().GetResult();
        workspace.UpdateReimbursementAsync(new(id, new DateOnly(2026, 9, 10), "材料费", "归档测试材料", 12000)).GetAwaiter().GetResult();
        workspace.SetReimbursementMilestonesAsync(Enum.GetValues<Milestone>().Select(m =>
            new Eiri.Reimbursement.Core.Reimbursements.SetReimbursementMilestoneCommand(id, m, DateTimeOffset.UtcNow)).ToArray()).GetAwaiter().GetResult();
        var main = (MainWindowViewModel)owner.DataContext;
        main.LoadAsync().GetAwaiter().GetResult();
        Exception? failure = null;
        bool opened = false;
        owner.Dispatcher.BeginInvoke(new Action(() =>
        {
            MainWindow? archiveWindow = null;
            try
            {
                archiveWindow = application.Windows.OfType<MainWindow>().Single(w => w != owner);
                opened = true;
                var vm = (MainWindowViewModel)archiveWindow.DataContext;
                Assert.True(vm.IsArchive);
                Assert.Same(owner, archiveWindow.Owner);
                archiveWindow.UpdateLayout();
                Assert.False(((Button)archiveWindow.FindName("CreateOrderButton")).IsVisible);
                var orders = (DataGrid)archiveWindow.FindName("OrdersGrid");
                Assert.Null(orders.ContextMenu);
                orders.SelectedItem = vm.Orders.Single(o => o.Id == order);
                archiveWindow.UpdateLayout();
                var orderTabs = (TabControl)archiveWindow.FindName("OrderDetailTabs");
                orderTabs.SelectedIndex = 1;
                archiveWindow.UpdateLayout();
                Assert.True(((TextBox)archiveWindow.FindName("ProductNamesEditor")).IsReadOnly);
                Assert.False(((ComboBox)archiveWindow.FindName("InvoicePlatformSelector")).IsEnabled);
                Assert.False(((FrameworkElement)archiveWindow.FindName("MaterialDropZones")).IsVisible);
                SavePreview(archiveWindow, "archive-order.png");
                var forms = (DataGrid)archiveWindow.FindName("ReimbursementsGrid");
                forms.SelectedItem = vm.Reimbursements.Single(r => r.Id == id);
                archiveWindow.UpdateLayout();
                var fields = (ReimbursementDetailView)archiveWindow.FindName("ReimbursementDetailFields");
                var tabs = (TabControl)fields.FindName("ReimbursementDetailTabs");
                tabs.SelectedIndex = 1;
                archiveWindow.UpdateLayout();
                Assert.True(((TextBox)fields.FindName("ContentInput")).IsReadOnly);
                Assert.True(((TextBox)fields.FindName("ContentInput")).IsEnabled);
                Assert.False(((FrameworkElement)fields.FindName("ReimbursementDropZone")).IsVisible);
                Assert.False(((Button)fields.FindName("DingTalkApprovalButton")).IsVisible);
                SavePreview(archiveWindow, "archive-form.png");
                archiveWindow.Width = 980;
                ThemeManager.Toggle(application.Resources);
                SavePreview(archiveWindow, "archive-narrow-dark.png");
                ThemeManager.Toggle(application.Resources);
                var restore = Assert.IsType<MenuItem>(Assert.Single(forms.ContextMenu!.Items.Cast<object>()));
                Assert.Equal("取消归档", restore.Header);
                forms.ContextMenu.IsOpen = true;
                archiveWindow.UpdateLayout();
                SavePreview(archiveWindow, "archive-context-menu.png");
                forms.ContextMenu.IsOpen = false;
                restore.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert.DoesNotContain(vm.Reimbursements, r => r.Id == id);
                Assert.Null(main.Orders.Single(o => o.Id == order).RefundedAt);
                SavePreview(archiveWindow, "archive-empty.png");
            }
            catch (Exception exception) { failure = exception; }
            finally { archiveWindow?.Close(); }
        }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var menu = (MenuItem)owner.FindName("ArchiveMenu");
        ((MenuItem)menu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.True(opened);
        Assert.Null(failure);
        Assert.Null(main.Reimbursements.Single(r => r.Id == id).Form.RefundedAt);
    }

    private static void VerifySubmissionModalSaveAndClose(MainWindow owner, MainWindowViewModel vm)
    {
        bool timedOut = false;
        bool saved = false;
        DingTalkSubmissionInfoWindow? dialog = null;
        var timeout = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        timeout.Tick += (_, _) => { timedOut = true; timeout.Stop(); dialog?.Close(); };
        owner.Dispatcher.BeginInvoke(new Action(() =>
        {
            dialog = Application.Current.Windows.OfType<DingTalkSubmissionInfoWindow>().Single();
            var buttons = FindVisualChildren<Button>(dialog).ToArray();
            buttons.Single(button => Equals(button.Content, "保存")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            saved = ((DingTalkSubmissionInfoViewModel)dialog.DataContext).StatusMessage == "已保存。";
            var close = buttons.Single(button => Equals(button.Content, "关闭"));
            var peer = new System.Windows.Automation.Peers.ButtonAutomationPeer(close);
            ((System.Windows.Automation.Provider.IInvokeProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();
        }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        try
        {
            timeout.Start();
            var menu = (MenuItem)owner.FindName("DingTalkMenu");
            menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "报销提审信息")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.True(saved, "Modal Save did not persist successfully.");
            Assert.DoesNotContain("无法读取报销提审信息", vm.StatusMessage);
            Assert.DoesNotContain("窗口操作失败", vm.StatusMessage);
            Assert.False(timedOut, "Close button did not close the modal.");
            Assert.False(dialog!.IsVisible, "Modal remained visible after Close.");
        }
        finally { timeout.Stop(); if (dialog?.IsVisible == true) dialog.Close(); }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is T result) yield return result;
            foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }

    private static void VerifyPendingSubmissionClose(MainWindow owner, SqliteReimbursementWorkspace workspace, bool failFirstSave)
    {
        var store = new PendingFormStore();
        var vm = new DingTalkSubmissionInfoViewModel(new TestDirectoryClient(), workspace, "test-token", new TestFormClient(), store);
        using var lifetime = new CancellationTokenSource();
        vm.InitializeAsync(lifetime.Token).GetAwaiter().GetResult();
        var dialog = new DingTalkSubmissionInfoWindow(vm) { Owner = owner };
        bool waitedForSave = false;
        bool preservedOnFailure = false;
        bool timedOut = false;
        Exception? callbackError = null;
        var timeout = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timeout.Tick += (_, _) => { timedOut = true; timeout.Stop(); store.FirstSave.TrySetResult(); dialog.Close(); };
        owner.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                Assert.True(vm.FormFields.Count > 0, "Form was not loaded: " + vm.StatusMessage);
                store.HoldNextSave = true;
                dialog.Close();
                waitedForSave = dialog.IsVisible && !dialog.IsEnabled;
                if (failFirstSave) store.FirstSave.SetException(new IOException("Test storage failure"));
                else store.FirstSave.SetResult();
                await vm.PendingFormSave;
                if (failFirstSave)
                {
                    while (dialog.IsVisible && !dialog.IsEnabled && !timedOut) await Task.Delay(10);
                    preservedOnFailure = dialog.IsVisible && dialog.IsEnabled && vm.StatusMessage.Contains("保存失败");
                    dialog.Close();
                }
            }
            catch (Exception exception) { callbackError = exception; store.FirstSave.TrySetResult(); dialog.Close(); }
        }), System.Windows.Threading.DispatcherPriority.Background);
        try { timeout.Start(); dialog.ShowDialog(); }
        finally { timeout.Stop(); }
        Assert.Null(callbackError);
        Assert.False(timedOut);
        Assert.True(waitedForSave, "Window did not wait for its pending save.");
        Assert.False(dialog.IsVisible);
        if (failFirstSave) Assert.True(preservedOnFailure, "Failed save did not keep an editable dialog for retry.");
    }

    private sealed class PendingFormStore : IDingTalkFormPrefillStore
    {
        public TaskCompletionSource FirstSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool HoldNextSave { get; set; }
        public Task<IReadOnlyDictionary<string, DingTalkPrefillValue>> GetFormPrefillAsync(string processCode, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, DingTalkPrefillValue>>(new Dictionary<string, DingTalkPrefillValue>());
        public Task SaveFormPrefillAsync(string processCode, IReadOnlyDictionary<string, DingTalkPrefillValue> values, CancellationToken cancellationToken = default)
        {
            if (!HoldNextSave) return Task.CompletedTask;
            HoldNextSave = false; return FirstSave.Task;
        }
    }

    private sealed class TestTokenClient : IDingTalkAccessTokenClient
    {
        public static readonly string Token = string.Concat(Enumerable.Repeat("test-access-token-", 16));
        public Task<DingTalkAccessToken> GetAccessTokenAsync(string clientId, string clientSecret, CancellationToken cancellationToken = default) => Task.FromResult(new DingTalkAccessToken(Token, 7200));
    }

    private sealed class TestDirectoryClient : IDingTalkDirectoryClient
    {
        public Task<IReadOnlyList<DingTalkDepartment>> GetDepartmentsAsync(string accessToken, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DingTalkDepartment>>([new(2, "研发部"), new(3, "财务部")]);
        public Task<IReadOnlyList<DingTalkUser>> GetUsersAsync(string accessToken, long deptId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DingTalkUser>>([new("first", "张三")]);
    }

    private static void AssertApprovalWindow(App application, MainWindow owner)
    {
        using (MemoryStream imageData = new())
        {
            var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(640, 480, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[640 * 480 * 4], 640 * 4);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap)); encoder.Save(imageData); imageData.Position = 0;
            var metadata = new DingTalkImageMetadataReader().Read(imageData, "test.PNG");
            Assert.Equal(640, metadata.Width); Assert.Equal(480, metadata.Height); Assert.Equal("png", metadata.Extension);
            imageData.Position = 0;
            Assert.Throws<InvalidOperationException>(() => new DingTalkImageMetadataReader().Read(imageData, "wrong.jpg"));
        }
        using var context = new ApprovalTestContext();
        var vm = context.Create();
        DingTalkApprovalWindow dialog = new(vm) { Owner = owner };
        dialog.Show(); dialog.UpdateLayout();
        dialog.PendingWork.GetAwaiter().GetResult();
        Assert.True(vm.IsLoaded, vm.StatusMessage);
        Assert.Single(vm.WorkflowNodes);
        Assert.Equal(vm.Fields.Count, Assert.IsType<ItemsControl>(dialog.FindName("FieldsList")).Items.Count);
        Assert.Contains(Descendants<TextBlock>(dialog), label => label.Text == "报销类型*");
        Assert.Contains(Descendants<Image>(dialog), image => image.Source is not null && image.ActualWidth > 0);
        Assert.Contains(Descendants<TextBlock>(dialog), label => label.Text == "上传成功");
        SavePreview(dialog, "dingtalk-approval-top.png");
        var departments = Assert.IsType<ComboBox>(dialog.FindName("DepartmentInput"));
        departments.IsDropDownOpen = true;
        Assert.Equal(2, departments.Items.Count);
        SavePreview(dialog, "dingtalk-approval-departments.png");
        departments.IsDropDownOpen = false;
        Assert.Null(dialog.FindName("GetFlowButton"));
        dialog.PendingWork.GetAwaiter().GetResult();
        Assert.Null(dialog.FindName("FlowOutput"));
        Assert.Null(dialog.FindName("RequestOutput"));
        Assert.Null(dialog.FindName("GenerateInstanceRequestButton"));
        Assert.Null(dialog.FindName("SelfSelectedNodeResult"));
        dialog.UpdateLayout();
        var workflow = Assert.IsType<ItemsControl>(dialog.FindName("WorkflowList"));
        var candidates = Assert.Single(Descendants<ComboBox>(workflow), c => c.IsVisible);
        candidates.SelectedIndex = 0;
        var scroll = Assert.IsType<ScrollViewer>(dialog.FindName("FormScroll"));
        scroll.ScrollToBottom(); dialog.UpdateLayout();
        SavePreview(dialog, "dingtalk-approval-flow.png");
        ThemeManager.Toggle(application.Resources);
        dialog.Width = 440; dialog.Height = 520;
        SavePreview(dialog, "dingtalk-approval-narrow-dark.png");
        ThemeManager.Toggle(application.Resources);
        var remove = Descendants<Button>(dialog).First(b => b.Content?.ToString() == "移除");
        remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Empty(vm.Fields.Single(f => f.IsPhoto).Images);
        Assert.Empty(vm.InstanceRequestJson);
        dialog.Close();
        context.Publisher.Error = true;
        var failedVm = context.Create(); failedVm.LoadAsync().GetAwaiter().GetResult();
        DingTalkApprovalWindow failed = new(failedVm) { Owner = owner };
        failed.Show(); failed.UpdateLayout();
        Assert.Contains(Descendants<TextBlock>(failed), label => label.Text.StartsWith("上传失败"));
        Assert.IsType<ScrollViewer>(failed.FindName("FormScroll")).ScrollToBottom();
        SavePreview(failed, "dingtalk-upload-failed.png");
        failed.Close(); context.Publisher.Error = false;
        using (var rowsJson = System.Text.Json.JsonDocument.Parse("""
            {"result":{"isForecastSuccess":true,"workflowActivityRules":[{
              "activityName":"抄送人","activityType":"target_select","workflowActor":{"actorType":"notifier","allowedMulti":true,"required":false,"actorSelectionRange":{}},
              "activityActioners":[{"userId":"a","name":"默认人员甲"},{"userId":"b","name":"默认人员乙"}]
            }]}}
            """))
        {
            context.Forecast.Response = rowsJson.RootElement.Clone();
            var rowsVm = context.Create(); rowsVm.LoadAsync().GetAwaiter().GetResult(); rowsVm.GetFlowAsync().GetAwaiter().GetResult();
            DingTalkApprovalWindow rowsWindow = new(rowsVm) { Owner = owner };
            rowsWindow.Show(); rowsWindow.UpdateLayout();
            var rowsList = Assert.IsType<ItemsControl>(rowsWindow.FindName("WorkflowList"));
            var node = Assert.Single(rowsVm.WorkflowNodes);
            Assert.Equal(2, node.People.Count);
            Assert.Equal("a", node.People[0].SelectedCandidate?.UserId);
            Assert.Equal("b", node.People[1].SelectedCandidate?.UserId);
            Assert.DoesNotContain(Descendants<ComboBox>(rowsList), c => c.IsVisible && System.Windows.Automation.AutomationProperties.GetName(c) == "审批人员部门");
            Assert.Single(Descendants<Button>(rowsList), b => b.Content?.ToString() == "添加人员").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            rowsWindow.UpdateLayout(); Assert.Equal(3, node.People.Count);
            rowsWindow.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            var row = node.People[2];
            var departmentCombo = Assert.Single(Descendants<ComboBox>(rowsList), c => c.DataContext == row && System.Windows.Automation.AutomationProperties.GetName(c) == "审批人员部门");
            departmentCombo.IsDropDownOpen = true; rowsWindow.PendingWork.GetAwaiter().GetResult();
            departmentCombo.IsDropDownOpen = false; departmentCombo.SelectedIndex = 0;
            rowsWindow.UpdateLayout();
            var personCombo = Assert.Single(Descendants<ComboBox>(rowsList), c => c.DataContext == row && System.Windows.Automation.AutomationProperties.GetName(c) == "审批人员");
            Assert.True(personCombo.IsEnabled, $"Departments={row.Departments.Count}, Items={departmentCombo.Items.Count}, SelectedIndex={departmentCombo.SelectedIndex}, Department={row.SelectedDepartment?.Name}, Busy={row.IsBusy}, Status={row.StatusMessage}");
            personCombo.IsDropDownOpen = true; rowsWindow.PendingWork.GetAwaiter().GetResult();
            personCombo.IsDropDownOpen = false; personCombo.SelectedIndex = 0;
            Assert.Equal("applicant", row.SelectedCandidate!.UserId);
            rowsWindow.UpdateLayout();
            var removeRow = Assert.Single(Descendants<Button>(rowsList), b => b.DataContext == row && b.Content?.ToString() == "移除");
            Assert.Equal(departmentCombo.ActualHeight + personCombo.ActualHeight + 6, removeRow.ActualHeight, 1);
            rowsList.BringIntoView(); rowsWindow.UpdateLayout();
            SavePreview(rowsWindow, "dingtalk-workflow-rows.png");
            rowsWindow.Width = 440; rowsWindow.UpdateLayout(); rowsList.BringIntoView();
            SavePreview(rowsWindow, "dingtalk-workflow-rows-narrow.png");
            Assert.Single(Descendants<Button>(rowsList), b => b.DataContext == row && b.Content?.ToString() == "移除").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(2, node.People.Count);
            using var singleDefaultJson = System.Text.Json.JsonDocument.Parse("""
                {"activityName":"默认审批人","activityType":"target_select","workflowActor":{"allowedMulti":false,"actorKey":"single","actorSelectionRange":{}},"activityActioners":[{"userId":"default","name":"默认人员"}]}
                """);
            var singleNode = new DingTalkWorkflowNodeViewModel(singleDefaultJson.RootElement);
            rowsVm.WorkflowNodes = new[] { singleNode }; rowsWindow.UpdateLayout();
            var modify = Assert.Single(Descendants<Button>(rowsList), b => b.IsVisible && b.Content?.ToString() == "修改");
            Assert.False(singleNode.People[0].ShowDepartment);
            modify.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); rowsWindow.UpdateLayout();
            Assert.True(singleNode.People[0].ShowDepartment); Assert.False(modify.IsVisible);
            SavePreview(rowsWindow, "dingtalk-default-person-edit.png");
            rowsWindow.Close(); context.Forecast.Response = null;
        }
        var submitClient = new DingTalkSubmitTests.Client();
        var submitVm = context.Create(approval: submitClient);
        DingTalkSubmitTests.Prepare(submitVm).GetAwaiter().GetResult();
        DingTalkApprovalWindow submitWindow = new(submitVm) { Owner = owner };
        submitWindow.Show(); submitWindow.UpdateLayout();
        var submitButton = Assert.IsType<Button>(submitWindow.FindName("SubmitApprovalButton"));
        Assert.True(submitButton.IsEnabled);
        submitButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        submitWindow.PendingWork.GetAwaiter().GetResult();
        submitWindow.UpdateLayout();
        Assert.Equal(1, submitClient.Calls); Assert.False(submitButton.IsEnabled);
        Assert.Null(submitWindow.FindName("InstanceIdOutput"));
        Assert.Equal("instance-test", context.Workspace.GetReimbursementAsync(context.Id).GetAwaiter().GetResult()!.Form.DingTalkInstanceId);
        Assert.IsType<ScrollViewer>(submitWindow.FindName("FormScroll")).ScrollToBottom();
        SavePreview(submitWindow, "dingtalk-submitted.png");
        submitWindow.Width = 440; submitWindow.UpdateLayout();
        SavePreview(submitWindow, "dingtalk-submitted-narrow.png");
        submitWindow.Close();
        var detailVm = new ReimbursementEditorViewModel(context.Workspace, context.Id);
        detailVm.LoadAsync().GetAwaiter().GetResult();
        var detailView = new ReimbursementDetailView { DataContext = detailVm };
        var instanceOutput = Assert.IsType<TextBox>(detailView.FindName("DingTalkInstanceIdOutput"));
        Assert.True(instanceOutput.IsReadOnly);
        Window detailWindow = new() { Content = detailView, Width = 560, Height = 700, Owner = owner };
        detailWindow.Show(); detailWindow.UpdateLayout();
        Assert.Single(Descendants<TabControl>(detailView)).SelectedIndex = 1;
        detailWindow.UpdateLayout();
        Assert.Equal("instance-test", instanceOutput.Text);
        Assert.True(instanceOutput.IsVisible);
        SavePreview(detailWindow, "reimbursement-instance-property.png");
        detailWindow.Close();
        if (Environment.GetEnvironmentVariable("EIRI_SCHEMA_EXAMPLE") is not null)
        {
            var actualVm = context.Create(forms: new TestFormClient());
            actualVm.LoadAsync().GetAwaiter().GetResult();
            Assert.True(actualVm.IsLoaded, actualVm.StatusMessage);
            Assert.Equal(new TestFormClient().GetReimbursementTemplateAsync("test-token").GetAwaiter().GetResult().AllFields.Count, actualVm.Fields.Count);
            Assert.Contains(actualVm.Fields, field => field.Definition.Label == "报销类型");
            Assert.Contains(actualVm.Fields, field => field.Definition.Label == "报销内容");
            DingTalkApprovalWindow actual = new(actualVm) { Owner = owner };
            actual.Show(); actual.UpdateLayout();
            SavePreview(actual, "dingtalk-approval-schema-top.png");
            Assert.IsType<ScrollViewer>(actual.FindName("FormScroll")).ScrollToBottom();
            SavePreview(actual, "dingtalk-approval-schema-bottom.png");
            actual.Close();
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private sealed class TestFormClient : IDingTalkFormClient
    {
        public Task<DingTalkFormTemplate> GetReimbursementTemplateAsync(string accessToken, CancellationToken cancellationToken = default)
        {
            string? example = Environment.GetEnvironmentVariable("EIRI_SCHEMA_EXAMPLE");
            string json = example is null ? """
                {"result":{"schemaContent":{"items":[
                {"componentName":"DDSelectField","props":{"id":"choice","label":"研究方向","options":["甲","乙"]}},
                {"componentName":"TextareaField","props":{"id":"text","label":"备注"}},
                {"componentName":"DDSelectField","props":{"id":"expense-type","label":"报销类型","options":["材料费"]}},
                {"componentName":"TextareaField","props":{"id":"expense-content","label":"报销内容"}},
                {"componentName":"DDMultiSelectField","props":{"id":"multi","label":"多个选项","options":["甲","乙"]}}
                ]}}}
                """ : File.ReadAllText(example);
            using var document = System.Text.Json.JsonDocument.Parse(json);
            return Task.FromResult(DingTalkFormSchema.Parse("test-process", document.RootElement));
        }
    }
}
