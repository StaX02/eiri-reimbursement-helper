using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
                TextBlock managementHeading = Assert.IsType<TextBlock>(
                    window.FindName("ManagementHeading"));
                Button batchImportButton = Assert.IsType<Button>(
                    window.FindName("BatchImportInvoicesButton"));
                MenuItem optionsMenu = Assert.IsType<MenuItem>(window.FindName("OptionsMenu"));
                MenuItem aboutMenu = Assert.IsType<MenuItem>(window.FindName("AboutMenu"));
                MenuItem toggleThemeMenuItem = Assert.IsType<MenuItem>(
                    window.FindName("ToggleThemeMenuItem"));
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
                Assert.Equal("批量导入发票", batchImportButton.Content);
                Assert.Equal("选项", optionsMenu.Header);
                Assert.Equal("关于", aboutMenu.Header);
                Assert.Equal("切换深色/浅色模式", toggleThemeMenuItem.Header);
                Assert.Equal(1, Grid.GetRow(detailActions));
                Assert.Equal(2, Grid.GetRow(materialDropZones));
                Assert.Equal(3, platformSelector.Items.Count);
                Assert.Equal(DataGridSelectionMode.Extended, ordersGrid.SelectionMode);
                Assert.Equal(DataGridSelectionUnit.FullRow, ordersGrid.SelectionUnit);
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

                viewModel.SelectedOrder = new OrderListItem(
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
                window.UpdateLayout();

                Assert.Equal(Visibility.Visible, detailPanel.Visibility);
                Assert.Equal("订单详情", detailHeading.Text);
                Assert.Equal(Visibility.Visible, detailActions.Visibility);
                detailTabs.SelectedItem = statusTab;
                window.UpdateLayout();
                Assert.DoesNotContain(
                    FindVisualChildren<Button>(statusTab),
                    button => Equals(button.Content, "保存状态"));

                viewModel.SetSelectedOrderCount(2);
                window.UpdateLayout();
                Assert.Equal("已选中多个订单", detailHeading.Text);
                Assert.Equal(Visibility.Collapsed, detailActions.Visibility);
                Assert.Equal(Visibility.Collapsed, materialDropZones.Visibility);
                Assert.Equal(Visibility.Collapsed, detailTabs.Visibility);

                toggleThemeMenuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                window.UpdateLayout();
                Assert.Equal(
                    Color.FromRgb(0x11, 0x18, 0x27),
                    Assert.IsType<SolidColorBrush>(window.Background).Color);
                toggleThemeMenuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

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
}
