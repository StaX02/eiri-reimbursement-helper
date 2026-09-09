using System.Windows;
using System.Windows.Controls;
using Eiri.Reimbursement.Core.DingTalk;
using Eiri.Reimbursement.Desktop.ViewModels;

namespace Eiri.Reimbursement.Desktop;

public partial class DingTalkSubmissionInfoWindow : Window
{
    private readonly DingTalkSubmissionInfoViewModel _viewModel;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _reopening;

    public DingTalkSubmissionInfoWindow(DingTalkSubmissionInfoViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Closed += (_, _) => _lifetime.Cancel();
    }

    private async void Department_OnOpened(object? sender, EventArgs e)
    {
        if (_reopening || _viewModel.IsBusy) return;
        DepartmentInput.IsDropDownOpen = false;
        await _viewModel.LoadDepartmentsAsync(_lifetime.Token);
        Reopen(DepartmentInput);
    }

    private async void User_OnOpened(object? sender, EventArgs e)
    {
        if (_reopening || !_viewModel.CanSelectUser) return;
        UserInput.IsDropDownOpen = false;
        await _viewModel.LoadUsersAsync(_lifetime.Token);
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
        await _viewModel.SelectDepartmentAsync(DepartmentInput.SelectedItem as DingTalkDepartment, _lifetime.Token);
    }

    private async void User_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel is null || _viewModel.IsBusy) return;
        await _viewModel.SelectUserAsync(UserInput.SelectedItem as DingTalkUser, _lifetime.Token);
    }
}
