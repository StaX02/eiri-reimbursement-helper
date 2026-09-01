using System.Windows;
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

    private async void SelectFilesButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Title = "选择订单截图和发票",
            Filter = "支持的材料|*.pdf;*.png;*.jpg;*.jpeg|PDF 发票|*.pdf|订单截图|*.png;*.jpg;*.jpeg",
            Multiselect = true,
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) == true && DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.ImportFilesAsync(dialog.FileNames);
        }
    }

    private void DropZone_OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void DropZone_OnDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel
            || e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        await viewModel.ImportFilesAsync(paths);
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
}
