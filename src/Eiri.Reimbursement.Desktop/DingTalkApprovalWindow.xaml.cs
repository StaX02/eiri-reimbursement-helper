using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using Eiri.Reimbursement.Core.DingTalk;
using Eiri.Reimbursement.Desktop.ViewModels;
using Microsoft.Win32;

namespace Eiri.Reimbursement.Desktop;

public partial class DingTalkApprovalWindow : Window
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DingTalkApprovalViewModel _vm;
    private bool _reopening;
    private Task _pendingWork = Task.CompletedTask;
    public Task PendingWork { get => Task.WhenAll(_pendingWork, _vm.PendingAutomaticFlow); private set => _pendingWork = value; }
    public DingTalkApprovalWindow(DingTalkApprovalViewModel vm)
    {
        InitializeComponent(); DataContext = _vm = vm;
        vm.EnableAutomaticFlow(_lifetime.Token);
        Loaded += async (_, _) => await (PendingWork = _vm.LoadAsync(_lifetime.Token));
        Closing += (_, e) => { if (_vm.IsSubmitting) e.Cancel = true; };
        Closed += (_, _) => { _lifetime.Cancel(); _vm.CancelUploads(); };
    }
    private async void Retry_OnClick(object sender, RoutedEventArgs e)
    {
        if (_vm.IsLoaded) { _vm.RefreshAutomaticFlow(); await _vm.PendingAutomaticFlow; }
        else await (PendingWork = _vm.LoadAsync(_lifetime.Token));
    }
    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();
    private async void SubmitApproval_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_vm.CanSubmit) return;
        await (PendingWork = _vm.SubmitAsync(_lifetime.Token));
        if (IsVisible && _vm.InvalidField is { } invalid && FieldsList.ItemContainerGenerator.ContainerFromItem(invalid) is FrameworkElement container) container.BringIntoView();
    }
    private async void SaveSubmission_OnClick(object sender, RoutedEventArgs e) => await (PendingWork = _vm.SaveSubmissionReceiptAsync());
    private async void ResetSubmission_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_vm.CanResetSubmission) return;
        if (MessageBox.Show(this, "请先在钉钉确认此报销审批未创建。重复提交可能产生重复审批。\n已确认没有对应审批实例，允许重新提交？",
            "核对提交结果", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        await (PendingWork = _vm.ResetSubmissionAfterVerificationAsync());
    }
    private void AddWorkflowPerson_OnClick(object sender, RoutedEventArgs e)
    {
        if (_vm.CanEdit && sender is Button { DataContext: DingTalkWorkflowNodeViewModel node }) node.AddPerson();
    }
    private void ModifyWorkflowPerson_OnClick(object sender, RoutedEventArgs e)
    {
        if (_vm.CanEdit && sender is Button { DataContext: DingTalkWorkflowPersonViewModel row }) row.ModifyDefault();
    }
    private void RemoveWorkflowPerson_OnClick(object sender, RoutedEventArgs e)
    {
        if (_vm.CanEdit && sender is Button { DataContext: DingTalkWorkflowPersonViewModel row, Tag: DingTalkWorkflowNodeViewModel node }) node.RemovePerson(row);
    }
    private async void WorkflowDepartment_OnOpened(object sender, EventArgs e)
    {
        if (_reopening || sender is not ComboBox { DataContext: DingTalkWorkflowPersonViewModel row } combo || !row.CanSelectDepartment) return;
        combo.IsDropDownOpen = false;
        await (PendingWork = row.LoadDepartmentsAsync(_lifetime.Token));
        ReopenWorkflow(combo, row);
    }
    private async void WorkflowPerson_OnOpened(object sender, EventArgs e)
    {
        if (_reopening || sender is not ComboBox { DataContext: DingTalkWorkflowPersonViewModel row } combo || !row.UsesDepartment || !row.CanSelectPerson) return;
        combo.IsDropDownOpen = false;
        await (PendingWork = row.LoadPeopleAsync(_lifetime.Token));
        ReopenWorkflow(combo, row);
    }
    private void ReopenWorkflow(ComboBox combo, DingTalkWorkflowPersonViewModel row)
    {
        if (_lifetime.IsCancellationRequested || !combo.IsVisible || !IsVisible || combo.Items.Count == 0 || row.StatusMessage.Length > 0) return;
        _reopening = true;
        try { combo.IsDropDownOpen = true; }
        finally { _reopening = false; }
    }
    private async void Department_OnOpened(object sender, EventArgs e)
    {
        if (_reopening || !_vm.Directory.CanSelectDepartment || sender is not ComboBox combo) return;
        combo.IsDropDownOpen = false;
        await _vm.Directory.LoadDepartmentsAsync(_lifetime.Token);
        Reopen(combo);
    }
    private async void User_OnOpened(object sender, EventArgs e)
    {
        if (_reopening || !_vm.Directory.CanSelectUser || sender is not ComboBox combo) return;
        combo.IsDropDownOpen = false;
        await _vm.Directory.LoadUsersAsync(_lifetime.Token);
        Reopen(combo);
    }
    private void Reopen(ComboBox combo)
    {
        if (_lifetime.IsCancellationRequested || !IsVisible || combo.Items.Count == 0 || !string.IsNullOrEmpty(_vm.Directory.StatusMessage)) return;
        _reopening = true;
        try { combo.IsDropDownOpen = true; }
        finally { _reopening = false; }
    }
    private async void Department_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_vm.Directory.IsBusy && sender is ComboBox { SelectedItem: DingTalkDepartment department })
            await _vm.Directory.SelectDepartmentAsync(department, _lifetime.Token);
    }
    private async void User_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_vm.Directory.IsBusy && sender is ComboBox { SelectedItem: DingTalkUser user })
            await _vm.Directory.SelectUserAsync(user, _lifetime.Token);
    }
    private void AddImage_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: DingTalkApprovalFieldViewModel field } || !_vm.CanEdit) return;
        OpenFileDialog dialog = new() { Title = "添加票据图片", Multiselect = true, Filter = "图片|*.png;*.jpg;*.jpeg;*.gif;*.bmp", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        foreach (string path in dialog.FileNames)
        {
            _vm.AddImage(field, path, _lifetime.Token);
        }
    }
    private void RemoveImage_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: DingTalkApprovalImageViewModel image, Tag: DingTalkApprovalFieldViewModel field })
            _vm.RemoveImage(field, image);
    }
}

public sealed class LocalImageConverter : IValueConverter
{
    public static LocalImageConverter Instance { get; } = new();
    public static BitmapImage Load(string path)
    {
        using var stream = File.OpenRead(path);
        BitmapImage image = new(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = 280; image.StreamSource = stream; image.EndInit(); image.Freeze(); return image;
    }
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try { return Load((string)value); } catch (Exception) { return DependencyProperty.UnsetValue; }
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
