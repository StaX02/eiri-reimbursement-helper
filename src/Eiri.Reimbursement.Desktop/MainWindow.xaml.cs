using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Eiri.Reimbursement.Core.Materials;
using Eiri.Reimbursement.Core.Orders;
using Eiri.Reimbursement.Desktop.ViewModels;
using Microsoft.Win32;

namespace Eiri.Reimbursement.Desktop;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private async void SelectInvoiceFilesButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Title = "选择发票",
            Filter = "PDF 发票|*.pdf",
            Multiselect = true,
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) == true && DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.ImportFilesAsync(dialog.FileNames, ManagedFileRole.InvoicePdf);
        }
    }

    private async void SelectSupportingFilesButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Title = "选择订单截图等辅助材料",
            Filter = "支持的辅助材料|*.pdf;*.png;*.jpg;*.jpeg|PDF|*.pdf|图片|*.png;*.jpg;*.jpeg",
            Multiselect = true,
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) == true && DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.ImportFilesAsync(dialog.FileNames, ManagedFileRole.OrderScreenshot);
        }
    }

    private void DropZone_OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void InvoiceDropZone_OnDrop(object sender, DragEventArgs e)
    {
        await ImportDroppedFilesAsync(e, ManagedFileRole.InvoicePdf);
    }

    private async void SupportingDropZone_OnDrop(object sender, DragEventArgs e)
    {
        await ImportDroppedFilesAsync(e, ManagedFileRole.OrderScreenshot);
    }

    private async Task ImportDroppedFilesAsync(DragEventArgs e, ManagedFileRole role)
    {
        if (DataContext is not MainWindowViewModel viewModel
            || e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        await viewModel.ImportFilesAsync(paths, role);
    }

    private async void DeleteOrderButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel { SelectedOrder: not null } viewModel)
        {
            return;
        }

        MessageBoxResult confirmation = MessageBox.Show(
            this,
            "将永久删除当前订单、发票记录以及受管资料库中的截图和 PDF。此操作无法撤销。",
            "确认删除订单",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation == MessageBoxResult.Yes)
        {
            await viewModel.DeleteSelectedOrderAsync();
        }
    }

    private void OrdersGrid_OnPreviewMouseRightButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source
            || ItemsControl.ContainerFromElement(OrdersGrid, source) is not DataGridRow row)
        {
            return;
        }

        if (!row.IsSelected)
        {
            OrdersGrid.SelectedItems.Clear();
            row.IsSelected = true;
        }

        OrdersGrid.CurrentItem = row.Item;
    }

    private async void DeleteOrdersMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        OrderListItem[] selectedOrders = OrdersGrid.SelectedItems
            .OfType<OrderListItem>()
            .ToArray();
        if (selectedOrders.Length == 0)
        {
            return;
        }

        string message = selectedOrders.Length == 1
            ? "将永久删除所选订单、发票记录以及受管资料库中的材料。此操作无法撤销。"
            : $"将永久删除所选的 {selectedOrders.Length} 个订单、发票记录以及受管资料库中的材料。此操作无法撤销。";
        MessageBoxResult confirmation = MessageBox.Show(
            this,
            message,
            selectedOrders.Length == 1 ? "确认删除订单" : "确认批量删除订单",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation == MessageBoxResult.Yes)
        {
            await viewModel.DeleteOrdersAsync(selectedOrders.Select(order => order.Id).ToArray());
        }
    }

    private async void SetOrdersSubmittedMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.SetOrdersMilestoneAsync(
                GetSelectedOrderIds(),
                Milestone.Submitted,
                isReached: true);
        }
    }

    private async void SetOrdersRefundedMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.SetOrdersMilestoneAsync(
                GetSelectedOrderIds(),
                Milestone.Refunded,
                isReached: true);
        }
    }

    private async void ClearOrdersSubmissionAndRefundMenuItem_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.ClearOrdersSubmissionAndRefundAsync(GetSelectedOrderIds());
        }
    }

    private OrderId[] GetSelectedOrderIds() => OrdersGrid.SelectedItems
        .OfType<OrderListItem>()
        .Select(order => order.Id)
        .ToArray();
}
