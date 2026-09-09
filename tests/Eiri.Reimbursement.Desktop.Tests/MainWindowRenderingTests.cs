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
                App application = new();
                application.InitializeComponent();
                string libraryRoot = Path.Combine(Path.GetTempPath(), "eiri-sidebar-render", Guid.NewGuid().ToString("N"));
                SqliteReimbursementWorkspace workspace = new(libraryRoot);
                workspace.InitializeAsync().GetAwaiter().GetResult();
                MainWindowViewModel viewModel = new(workspace);
                MainWindow window = new(viewModel);
                window.Show();
                window.UpdateLayout();
                Menu topMenu = Assert.IsType<Menu>(window.FindName("TopMenuBar"));
                Assert.Equal("钉钉", Assert.IsType<MenuItem>(topMenu.Items[0]).Header);
                Assert.Equal("选项", Assert.IsType<MenuItem>(topMenu.Items[1]).Header);
                DingTalkConnectionWindow connectionWindow = new(new TestTokenClient(), workspace) { Owner = window };
                connectionWindow.Show();
                Button connect = Assert.IsType<Button>(connectionWindow.FindName("ConnectButton"));
                connect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Contains("请填写", Assert.IsType<TextBlock>(connectionWindow.FindName("StatusText")).Text);
                Assert.IsType<TextBox>(connectionWindow.FindName("ClientIdInput")).Text = "test-client";
                Assert.IsType<PasswordBox>(connectionWindow.FindName("ClientSecretInput")).Password = "test-secret";
                connect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(TestTokenClient.Token, Assert.IsType<TextBox>(connectionWindow.FindName("AccessTokenOutput")).Text);
                Assert.Equal(TestTokenClient.Token, workspace.GetDingTalkConnectionAsync().GetAwaiter().GetResult()!.AccessToken);
                SavePreview(connectionWindow, "dingtalk-connection.png");
                ThemeManager.Toggle(application.Resources);
                SavePreview(connectionWindow, "dingtalk-connection-dark.png");
                ThemeManager.Toggle(application.Resources);
                connectionWindow.Close();
                DingTalkSubmissionInfoViewModel submissionVm = new(new TestDirectoryClient(), workspace, "test-token");
                submissionVm.InitializeAsync().GetAwaiter().GetResult();
                DingTalkSubmissionInfoWindow submissionWindow = new(submissionVm) { Owner = window };
                submissionWindow.Show();
                submissionWindow.UpdateLayout();
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
                ThemeManager.Toggle(application.Resources);
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

        Assert.True(uiThread.Join(TimeSpan.FromSeconds(15)), "UI rendering did not complete in time.");
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

    private sealed class TestTokenClient : IDingTalkAccessTokenClient
    {
        public static readonly string Token = string.Concat(Enumerable.Repeat("test-access-token-", 16));
        public Task<string> GetAccessTokenAsync(string clientId, string clientSecret, CancellationToken cancellationToken = default) => Task.FromResult(Token);
    }

    private sealed class TestDirectoryClient : IDingTalkDirectoryClient
    {
        public Task<IReadOnlyList<DingTalkDepartment>> GetDepartmentsAsync(string accessToken, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DingTalkDepartment>>([new(2, "研发部"), new(3, "财务部")]);
        public Task<IReadOnlyList<DingTalkUser>> GetUsersAsync(string accessToken, long deptId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DingTalkUser>>([new("first", "张三")]);
    }
}
