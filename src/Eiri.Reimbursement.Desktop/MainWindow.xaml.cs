using System.IO;
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

    public void RefreshOrdersList() => OrdersGrid.Items.Refresh();

    private void MinimizeWindowButton_OnClick(object sender, RoutedEventArgs e) =>
        SystemCommands.MinimizeWindow(this);

    private void MaximizeRestoreWindowButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
        }
        else
        {
            SystemCommands.MaximizeWindow(this);
        }
    }

    private void CloseWindowButton_OnClick(object sender, RoutedEventArgs e) =>
        SystemCommands.CloseWindow(this);

    private void BatchImportInvoicesButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            BatchInvoiceImportWindow dialog = new(viewModel)
            {
                Owner = this,
            };
            dialog.ShowDialog();
        }
    }

    private async void ExportReimbursementFilesButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        OrderId[] orderIds = GetSelectedOrderIds();
        if (orderIds.Length == 0)
        {
            return;
        }

        OpenFolderDialog dialog = new()
        {
            Title = "选择报销资料导出位置",
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        MessageBoxResult confirmation = MessageBox.Show(
            this,
            $"将在以下文件夹中创建发票图片、发票原件、报销辅助材料和 CSV：\n\n{dialog.FolderName}\n\n确认继续吗？",
            "确认报销资料导出位置",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);
        if (confirmation == MessageBoxResult.Yes)
        {
            await viewModel.ExportOrdersAsync(orderIds, dialog.FolderName);
        }
    }

    private void ToggleThemeMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        ThemeManager.Toggle(Application.Current.Resources);
    }

    private void AboutAuthorMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        AuthorInfoWindow dialog = new()
        {
            Owner = this,
        };
        dialog.ShowDialog();
    }

    private async void ExportDataMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        SaveFileDialog dialog = new()
        {
            Title = "导出数据",
            Filter = "Eiri 数据备份包|*.eirbackup",
            DefaultExt = ".eirbackup",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"Eiri-数据备份-{DateTime.Now:yyyyMMdd-HHmmss}.eirbackup",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await viewModel.ExportDataAsync(dialog.FileName);
            MessageBox.Show(
                this,
                $"订单数据库和受管文件已保存到：\n\n{dialog.FileName}",
                "数据已导出",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"数据导出失败，未生成可用的备份包。\n\n{exception.Message}",
                "无法导出数据",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void ImportDataMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        OpenFileDialog dialog = new()
        {
            Title = "导入数据",
            Filter = "Eiri 数据备份包|*.eirbackup;*.zip|所有文件|*.*",
            Multiselect = false,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (viewModel.HasOrders)
        {
            MessageBoxResult confirmation = MessageBox.Show(
                this,
                "当前资料库已有订单。导入将用备份包中的数据库和受管文件完整替换当前资料库，此操作无法撤销。\n\n是否继续导入？",
                "导入将替换现有数据",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }
        }

        try
        {
            await viewModel.ImportDataAsync(dialog.FileName);
            RefreshOrdersList();
            MessageBox.Show(
                this,
                "订单数据库和受管文件已成功导入。",
                "数据已导入",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (InvalidDataException exception)
        {
            MessageBox.Show(
                this,
                $"所选文件不是有效的 Eiri 数据备份包，未导入任何数据。\n\n{exception.Message}",
                "无法导入数据",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"数据导入未完成。请重试；若问题持续，请重启软件后检查资料库。\n\n{exception.Message}",
                "无法导入数据",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
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

    private void OrdersGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SetSelectedOrders(
                OrdersGrid.SelectedItems.OfType<OrderListItem>().ToArray());
        }
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

    private async void SubmittedStatusCheckBox_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox
            && DataContext is MainWindowViewModel { SelectedOrder: { } order } viewModel)
        {
            await viewModel.SetOrdersMilestoneAsync(
                [order.Id],
                Milestone.Submitted,
                checkBox.IsChecked == true);
        }
    }

    private async void RefundedStatusCheckBox_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox
            && DataContext is MainWindowViewModel { SelectedOrder: { } order } viewModel)
        {
            await viewModel.SetOrdersMilestoneAsync(
                [order.Id],
                Milestone.Refunded,
                checkBox.IsChecked == true);
        }
    }

    private OrderId[] GetSelectedOrderIds() => OrdersGrid.SelectedItems
        .OfType<OrderListItem>()
        .Select(order => order.Id)
        .ToArray();
}
