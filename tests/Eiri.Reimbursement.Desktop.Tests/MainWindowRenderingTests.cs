using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Eiri.Reimbursement.Core.Materials;
using Eiri.Reimbursement.Core.Orders;
using Eiri.Reimbursement.Desktop;
using Eiri.Reimbursement.Desktop.ViewModels;
using Eiri.Reimbursement.Infrastructure.Sqlite;

namespace Eiri.Reimbursement.Desktop.Tests;

public sealed class MainWindowRenderingTests
{
    [Fact]
    public void MainWindowReflectsOrderSelectionAndRendersMaterials()
    {
        Exception? renderingException = null;
        Thread uiThread = new(() =>
        {
            try
            {
                App application = new();
                application.InitializeComponent();
                SqliteReimbursementWorkspace workspace = new(Path.GetTempPath());
                MainWindowViewModel viewModel = new(workspace)
                {
                    Materials = new ObservableCollection<MaterialItemViewModel>(
                    [
                        new(new ManagedMaterial(
                            ManagedFileId.New(),
                            ManagedFileRole.InvoicePdf,
                            "invoice.pdf",
                            "invoice.pdf",
                            "application/pdf",
                            1024,
                            new string('a', 64),
                            MaterialProcessingState.Pending,
                            DateTimeOffset.UtcNow)),
                    ]),
                };
                MainWindow window = new(viewModel);
                window.Show();
                window.UpdateLayout();
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
                TextBlock managementHeading = Assert.IsType<TextBlock>(
                    window.FindName("ManagementHeading"));
                Button batchImportButton = Assert.IsType<Button>(
                    window.FindName("BatchImportInvoicesButton"));
                Button exportButton = Assert.IsType<Button>(
                    window.FindName("ExportReimbursementFilesButton"));
                Button createOrderButton = Assert.IsType<Button>(
                    window.FindName("CreateOrderButton"));
                Border topToolBar = Assert.IsType<Border>(window.FindName("TopToolBar"));
                Border titleBar = Assert.IsType<Border>(window.FindName("TitleBar"));
                StackPanel titleBarIdentity = Assert.IsType<StackPanel>(
                    window.FindName("TitleBarIdentity"));
                TextBlock windowTitle = Assert.IsType<TextBlock>(window.FindName("WindowTitle"));
                TextBlock windowSubtitle = Assert.IsType<TextBlock>(window.FindName("WindowSubtitle"));
                Button minimizeWindowButton = Assert.IsType<Button>(
                    window.FindName("MinimizeWindowButton"));
                Button maximizeRestoreWindowButton = Assert.IsType<Button>(
                    window.FindName("MaximizeRestoreWindowButton"));
                Button closeWindowButton = Assert.IsType<Button>(window.FindName("CloseWindowButton"));
                Menu topMenuBar = Assert.IsType<Menu>(window.FindName("TopMenuBar"));
                MenuItem optionsMenu = Assert.IsType<MenuItem>(window.FindName("OptionsMenu"));
                MenuItem aboutMenu = Assert.IsType<MenuItem>(window.FindName("AboutMenu"));
                MenuItem toggleThemeMenuItem = Assert.IsType<MenuItem>(
                    window.FindName("ToggleThemeMenuItem"));
                MenuItem importDataMenuItem = Assert.IsType<MenuItem>(
                    window.FindName("ImportDataMenuItem"));
                MenuItem exportDataMenuItem = Assert.IsType<MenuItem>(
                    window.FindName("ExportDataMenuItem"));
                Assert.NotNull(window.FindName("InvoiceDropZone"));
                Assert.NotNull(window.FindName("SupportingDropZone"));
                Assert.NotNull(window.FindName("InvoicePlatformSelector"));
                Assert.NotNull(window.FindName("ProductNamesEditor"));
                FrameworkElement detailPanel = Assert.IsAssignableFrom<FrameworkElement>(
                    window.FindName("OrderDetailPanel"));
                ComboBox platformSelector = Assert.IsType<ComboBox>(
                    window.FindName("PlatformSelector"));
                DataGrid ordersGrid = Assert.IsType<DataGrid>(window.FindName("OrdersGrid"));
                FrameworkElement detailActions = Assert.IsAssignableFrom<FrameworkElement>(
                    window.FindName("OrderDetailActions"));
                FrameworkElement materialDropZones = Assert.IsAssignableFrom<FrameworkElement>(
                    window.FindName("MaterialDropZones"));
                TextBlock detailHeading = Assert.IsType<TextBlock>(
                    window.FindName("OrderDetailHeading"));
                Assert.Equal("报销管理", managementHeading.Text);
                Assert.Equal("发票报销助手", windowTitle.Text);
                Assert.Equal("本地资料库", windowSubtitle.Text);
                Assert.Equal(WindowStyle.None, window.WindowStyle);
                Assert.Null(window.Icon);
                WindowChrome chrome = Assert.IsType<WindowChrome>(WindowChrome.GetWindowChrome(window));
                Assert.Equal(44, chrome.CaptionHeight);
                Assert.False(chrome.UseAeroCaptionButtons);
                Assert.Equal(new Thickness(6), chrome.ResizeBorderThickness);
                Assert.Equal("最小化窗口", AutomationProperties.GetName(minimizeWindowButton));
                Assert.Equal(
                    "最大化或还原窗口",
                    AutomationProperties.GetName(maximizeRestoreWindowButton));
                Assert.Equal("关闭窗口", AutomationProperties.GetName(closeWindowButton));
                Assert.Equal(Dock.Top, DockPanel.GetDock(titleBar));
                window.Width = window.MinWidth;
                window.Height = window.MinHeight;
                window.UpdateLayout();
                double titleIdentityRight = titleBarIdentity
                    .TranslatePoint(new Point(titleBarIdentity.ActualWidth, 0), titleBar)
                    .X;
                double minimizeButtonLeft = minimizeWindowButton
                    .TranslatePoint(new Point(0, 0), titleBar)
                    .X;
                Assert.True(titleIdentityRight <= minimizeButtonLeft);
                Assert.Equal("批量导入发票", batchImportButton.Content);
                Assert.Equal("导出报销资料", exportButton.Content);
                Assert.Equal(
                    Colors.White,
                    Assert.IsType<SolidColorBrush>(createOrderButton.Foreground).Color);
                TextBlock createOrderText = Assert.IsType<TextBlock>(createOrderButton.Content);
                Assert.Equal("新建订单", createOrderText.Text);
                Assert.Equal(
                    Colors.White,
                    Assert.IsType<SolidColorBrush>(createOrderText.Foreground).Color);
                ContentPresenter createOrderContent = FindVisualChildren<ContentPresenter>(
                    createOrderButton).Single(
                        presenter => ReferenceEquals(presenter.Content, createOrderText));
                Assert.Equal(
                    Colors.White,
                    Assert.IsType<SolidColorBrush>(
                        TextElement.GetForeground(createOrderContent)).Color);
                Assert.Equal(Dock.Top, DockPanel.GetDock(topToolBar));
                Assert.Equal(HorizontalAlignment.Left, topMenuBar.HorizontalAlignment);
                Grid topToolBarContent = Assert.IsType<Grid>(topToolBar.Child);
                Assert.Contains(topMenuBar, topToolBarContent.Children.OfType<Menu>());
                Assert.Equal(
                    [optionsMenu, aboutMenu],
                    topMenuBar.Items.OfType<MenuItem>());
                Assert.Equal("选项", optionsMenu.Header);
                Assert.Equal("关于", aboutMenu.Header);
                Assert.Equal("导入数据", importDataMenuItem.Header);
                Assert.Equal("导出数据", exportDataMenuItem.Header);
                Assert.Equal("切换深色/浅色模式", toggleThemeMenuItem.Header);
                Assert.Equal(
                    ["导入数据", "导出数据", "切换深色/浅色模式"],
                    optionsMenu.Items.OfType<MenuItem>().Select(item => item.Header));
                Assert.Equal(1, Grid.GetRow(detailActions));
                Assert.Equal(2, Grid.GetRow(materialDropZones));
                Assert.Equal(3, platformSelector.Items.Count);
                Assert.Equal(DataGridSelectionMode.Extended, ordersGrid.SelectionMode);
                Assert.Equal(DataGridSelectionUnit.FullRow, ordersGrid.SelectionUnit);
                Assert.Equal(100, ordersGrid.Columns[1].Width.Value);
                Assert.Equal(DataGridLengthUnitType.Pixel, ordersGrid.Columns[1].Width.UnitType);
                Assert.All(
                    ordersGrid.Columns.Skip(6).Take(3),
                    column =>
                    {
                        Assert.Equal(104, column.Width.Value);
                        Assert.Equal(DataGridLengthUnitType.Pixel, column.Width.UnitType);
                    });
                Assert.Equal(ordersGrid.Columns[6].ActualWidth, ordersGrid.Columns[8].ActualWidth);
                DataGridRow loadedRow = Assert.IsType<DataGridRow>(
                    ordersGrid.ItemContainerGenerator.ContainerFromIndex(0));
                DataGridCell[] loadedCells = FindVisualChildren<DataGridCell>(loadedRow).ToArray();
                Assert.All(
                    loadedCells,
                    cell => Assert.Equal(HorizontalAlignment.Center, cell.HorizontalContentAlignment));
                DataGridCell merchantCell = loadedCells.Single(cell => cell.Column.DisplayIndex == 2);
                Assert.Empty(FindVisualChildren<ComboBox>(merchantCell));
                Assert.Equal(
                    "商家甲等",
                    Assert.Single(FindVisualChildren<TextBlock>(merchantCell)).Text);
                DataGridCell invoiceNumberCell = loadedCells.Single(
                    cell => cell.Column.DisplayIndex == 5);
                Assert.All(
                    FindVisualChildren<TextBlock>(invoiceNumberCell),
                    text => Assert.Equal(TextAlignment.Center, text.TextAlignment));
                DataGridCell refundedCell = FindVisualChildren<DataGridCell>(loadedRow)
                    .Single(cell => cell.Column.DisplayIndex == 8);
                TextBlock refundedText = Assert.Single(FindVisualChildren<TextBlock>(refundedCell));
                Assert.True(
                    refundedCell.ActualWidth >= refundedText.DesiredSize.Width,
                    $"返款日期被截断：列宽 {refundedCell.ActualWidth}，内容需要 {refundedText.DesiredSize.Width}。");
                Assert.Equal(72, ordersGrid.Columns[0].Width.Value);
                Assert.Equal(DataGridLengthUnitType.Pixel, ordersGrid.Columns[0].Width.UnitType);
                Assert.Equal(
                    ["设为已提交", "设为已返款", "清空提交及返款状态", "删除订单"],
                    Assert.IsType<ContextMenu>(ordersGrid.ContextMenu).Items
                        .OfType<MenuItem>()
                        .Select(item => item.Header));
                TabControl detailTabs = Assert.IsType<TabControl>(window.FindName("OrderDetailTabs"));
                Assert.Equal(
                    ["材料", "发票", "提交/返款状态"],
                    detailTabs.Items.OfType<TabItem>().Select(tab => tab.Header));
                TabItem statusTab = detailTabs.Items.OfType<TabItem>().Last();
                Assert.NotNull(window.FindName("SubmittedStatusCheckBox"));
                Assert.NotNull(window.FindName("RefundedStatusCheckBox"));
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
                Assert.Equal("订单详情", detailHeading.Text);
                Assert.Equal(Visibility.Visible, detailActions.Visibility);
                CaptureIfRequested(window);
                toggleThemeMenuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                window.UpdateLayout();
                Assert.Equal(
                    Color.FromRgb(0x11, 0x17, 0x15),
                    Assert.IsType<SolidColorBrush>(window.Background).Color);
                ScrollBar ordersHorizontalScrollBar = FindVisualChildren<ScrollBar>(ordersGrid)
                    .Single(scrollBar => scrollBar.Orientation == Orientation.Horizontal);
                Assert.Equal(
                    Color.FromRgb(0x11, 0x17, 0x15),
                    Assert.IsType<SolidColorBrush>(ordersHorizontalScrollBar.Background).Color);
                Thumb ordersScrollThumb = Assert.Single(
                    FindVisualChildren<Thumb>(ordersHorizontalScrollBar));
                Border ordersScrollThumbSurface = Assert.Single(
                    FindVisualChildren<Border>(ordersScrollThumb));
                Assert.Equal(
                    Color.FromRgb(0x52, 0x62, 0x5C),
                    Assert.IsType<SolidColorBrush>(ordersScrollThumbSurface.Background).Color);
                CaptureIfRequested(window, "-dark");
                toggleThemeMenuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                detailTabs.SelectedItem = statusTab;
                window.UpdateLayout();
                Assert.DoesNotContain(
                    FindVisualChildren<Button>(statusTab),
                    button => Equals(button.Content, "保存状态"));

                viewModel.SetSelectedOrders(
                    [selectedOrder, selectedOrder with { Id = OrderId.New() }]);
                window.UpdateLayout();
                Assert.Equal("已选中多个订单", detailHeading.Text);
                Assert.Equal(Visibility.Collapsed, detailActions.Visibility);
                Assert.Equal(Visibility.Collapsed, materialDropZones.Visibility);
                Assert.Equal(Visibility.Collapsed, detailTabs.Visibility);

                BatchInvoiceImportWindow batchWindow = new(viewModel);
                Border batchDropZone = Assert.IsType<Border>(
                    batchWindow.FindName("BatchInvoiceDropZone"));
                Assert.True(batchDropZone.AllowDrop);
                batchWindow.Close();
                window.Close();
            }
            catch (Exception exception)
            {
                renderingException = exception;
            }
        });
        uiThread.SetApartmentState(ApartmentState.STA);

        uiThread.Start();

        Assert.True(uiThread.Join(TimeSpan.FromSeconds(5)), "UI rendering did not complete in time.");
        Assert.Null(renderingException);
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (T descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static void CaptureIfRequested(Window window, string suffix = "")
    {
        string? capturePath = Environment.GetEnvironmentVariable("EIRI_UI_CAPTURE_PATH");
        if (string.IsNullOrWhiteSpace(capturePath))
        {
            return;
        }

        if (suffix.Length > 0)
        {
            string extension = Path.GetExtension(capturePath);
            capturePath = Path.Combine(
                Path.GetDirectoryName(capturePath)!,
                $"{Path.GetFileNameWithoutExtension(capturePath)}{suffix}{extension}");
        }

        int width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        int height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        RenderTargetBitmap bitmap = new(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(capturePath)!);
        using FileStream stream = File.Create(capturePath);
        encoder.Save(stream);
    }
}
