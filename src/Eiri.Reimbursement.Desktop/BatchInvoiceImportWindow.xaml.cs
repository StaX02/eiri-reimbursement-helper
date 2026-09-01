using System.ComponentModel;
using System.Windows;
using Eiri.Reimbursement.Desktop.ViewModels;

namespace Eiri.Reimbursement.Desktop;

public partial class BatchInvoiceImportWindow : Window
{
    private bool _isImporting;
    private bool _allowClose;

    public BatchInvoiceImportWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void BatchInvoiceDropZone_OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = !_isImporting && e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void BatchInvoiceDropZone_OnDrop(object sender, DragEventArgs e)
    {
        if (_isImporting
            || DataContext is not MainWindowViewModel viewModel
            || e.Data.GetData(DataFormats.FileDrop) is not string[] paths
            || paths.Length == 0)
        {
            return;
        }

        _isImporting = true;
        BatchInvoiceDropZone.IsEnabled = false;
        ImportProgress.Visibility = Visibility.Visible;
        ImportStatusText.Text = $"正在导入并解析 {paths.Length} 张发票…";

        BatchInvoiceImportResult result = await viewModel.BatchImportInvoicesAsync(paths);
        if (result.FailedFileNames.Count > 0)
        {
            MessageBox.Show(
                this,
                "以下文件未能成功解析：\n\n" + string.Join(Environment.NewLine, result.FailedFileNames),
                "批量导入警告",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        _allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isImporting && !_allowClose)
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }
}
