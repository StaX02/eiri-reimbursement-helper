using System.Windows;
using System.Windows.Controls;
using Eiri.Reimbursement.Core.DingTalk;
using Eiri.Reimbursement.Desktop.ViewModels;

namespace Eiri.Reimbursement.Desktop;

public partial class DingTalkSubmissionInfoView : UserControl
{
    private readonly DingTalkSubmissionInfoViewModel _viewModel;
    private CancellationTokenSource _lifetime = new();
    private Task _pendingWork = Task.CompletedTask;
    private bool _reopening;

    public DingTalkSubmissionInfoView(DingTalkSubmissionInfoViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

    }

    public DingTalkSubmissionInfoViewModel ViewModel => _viewModel;
    public Task LoadAsync() => _pendingWork = _viewModel.LoadFormAsync(_lifetime.Token);
    public Task<bool> SaveAsync() => _viewModel.SaveFormAsync();
    public void Cancel() => _lifetime.Cancel();
    public async Task<bool> SaveBeforeClosingAsync()
    {
        _lifetime.Cancel();
        await _pendingWork;
        _lifetime.Dispose();
        _lifetime = new();
        return await SaveAsync();
    }

    private async void SaveForm_OnClick(object sender, RoutedEventArgs e) => await _viewModel.SaveFormAsync();
    private async void ReloadForm_OnClick(object sender, RoutedEventArgs e) => await LoadAsync();

    private async void Department_OnOpened(object? sender, EventArgs e)
    {
        if (_reopening || _viewModel.IsBusy) return;
        DepartmentInput.IsDropDownOpen = false;
        await (_pendingWork = _viewModel.LoadDepartmentsAsync(_lifetime.Token));
        Reopen(DepartmentInput);
    }

    private async void User_OnOpened(object? sender, EventArgs e)
    {
        if (_reopening || !_viewModel.CanSelectUser) return;
        UserInput.IsDropDownOpen = false;
        await (_pendingWork = _viewModel.LoadUsersAsync(_lifetime.Token));
        Reopen(UserInput);
    }

    private void Reopen(ComboBox comboBox)
    {
        if (_lifetime.IsCancellationRequested || !IsVisible || comboBox.Items.Count == 0 || !string.IsNullOrEmpty(_viewModel.StatusMessage)) return;
        _reopening = true;
        try { comboBox.IsDropDownOpen = true; }
        finally { _reopening = false; }
    }

    private async void Department_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel is null || _viewModel.IsBusy) return;
        await (_pendingWork = _viewModel.SelectDepartmentAsync(DepartmentInput.SelectedItem as DingTalkDepartment, _lifetime.Token));
    }

    private async void User_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel is null || _viewModel.IsBusy) return;
        await (_pendingWork = _viewModel.SelectUserAsync(UserInput.SelectedItem as DingTalkUser, _lifetime.Token));
    }
}
