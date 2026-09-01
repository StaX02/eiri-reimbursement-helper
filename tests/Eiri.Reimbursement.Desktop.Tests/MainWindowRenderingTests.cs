using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
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
                Assert.NotNull(window.FindName("InvoiceDropZone"));
                Assert.NotNull(window.FindName("SupportingDropZone"));
                FrameworkElement detailPanel = Assert.IsAssignableFrom<FrameworkElement>(
                    window.FindName("OrderDetailPanel"));
                ComboBox platformSelector = Assert.IsType<ComboBox>(
                    window.FindName("PlatformSelector"));
                DataGrid ordersGrid = Assert.IsType<DataGrid>(window.FindName("OrdersGrid"));
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
}
