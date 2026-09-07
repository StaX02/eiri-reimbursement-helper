using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Eiri.Reimbursement.Core.Reimbursements;
using Eiri.Reimbursement.Desktop.ViewModels;
using Microsoft.Win32;

namespace Eiri.Reimbursement.Desktop;

public partial class ReimbursementDetailView : UserControl
{
    public ReimbursementDetailView() => InitializeComponent();
    private async void AddFiles_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ReimbursementEditorViewModel editor || !editor.CanEdit) return;
        OpenFileDialog dialog = new() { Title = "添加报销单文件", Multiselect = true, Filter = "所有文件|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) await editor.ImportAsync(dialog.FileNames);
    }
    private void Files_OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DataContext is ReimbursementEditorViewModel { CanEdit: true } && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private async void Files_OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (DataContext is ReimbursementEditorViewModel editor && e.Data.GetData(DataFormats.FileDrop) is string[] paths) await editor.ImportAsync(paths);
    }
    private void Attachments_OnDoubleClick(object sender, MouseButtonEventArgs e) => OpenAttachment();
    private void Attachments_OnKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { OpenAttachment(); e.Handled = true; } }
    private void OpenAttachment()
    {
        if (DataContext is not ReimbursementEditorViewModel editor || AttachmentList.SelectedItem is not ReimbursementAttachment file) return;
        try { Process.Start(new ProcessStartInfo(file.ManagedPath) { UseShellExecute = true }); }
        catch (Exception exception) { editor.StatusMessage = $"无法打开附件：{exception.Message}"; }
    }
}
