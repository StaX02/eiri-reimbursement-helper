using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Eiri.Reimbursement.Core.Reimbursements;
using Eiri.Reimbursement.Desktop.ViewModels;
using Microsoft.Win32;

namespace Eiri.Reimbursement.Desktop;

public partial class ReimbursementDetailWindow : Window
{
    private readonly ReimbursementEditorViewModel _viewModel;
    public ReimbursementDetailWindow(ReimbursementEditorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        Closing += OnClosing;
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_viewModel.IsBusy) { e.Cancel = true; return; }
        if (_viewModel.HasChanges)
            e.Cancel = MessageBox.Show(this, "尚有未保存的修改，关闭将放弃这些修改。是否关闭？", "未保存的报销单", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes;
    }
    private async void Save_OnClick(object sender, RoutedEventArgs e) => await _viewModel.SaveAsync();
    private async void AddFiles_OnClick(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new() { Title = "添加报销单文件", Multiselect = true, Filter = "所有文件|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) await _viewModel.ImportAsync(dialog.FileNames);
    }
    private void Files_OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = _viewModel.CanEdit && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private async void Files_OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) await _viewModel.ImportAsync(paths);
    }
    private void Attachments_OnDoubleClick(object sender, MouseButtonEventArgs e) => OpenAttachment();
    private void Attachments_OnKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { OpenAttachment(); e.Handled = true; } }
    private void OpenAttachment()
    {
        if (AttachmentList.SelectedItem is not ReimbursementAttachment file) return;
        try { Process.Start(new ProcessStartInfo(file.ManagedPath) { UseShellExecute = true }); }
        catch (Exception exception) { _viewModel.StatusMessage = $"无法打开附件：{exception.Message}"; }
    }
}
