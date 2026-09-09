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
        Loaded += async (_, _) => await viewModel.LoadFormAsync(_lifetime.Token);
        Closing += OnClosing;
        Closed += (_, _) => _lifetime.Cancel();
    }

    private bool _closeReady;
    private bool _savingToClose;
    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_closeReady) return;
        e.Cancel = true;
        if (_savingToClose) return;
        _savingToClose = true;
        try
        {
            if (await _viewModel.SaveFormAsync()) { _lifetime.Cancel(); _closeReady = true; Close(); }
        }
        finally { _savingToClose = false; }
    }

    private async void SaveForm_OnClick(object sender, RoutedEventArgs e) => await _viewModel.SaveFormAsync();
    private async void ReloadForm_OnClick(object sender, RoutedEventArgs e) => await _viewModel.LoadFormAsync(_lifetime.Token);

    private void MultiChoice_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBox) comboBox.GetBindingExpression(ComboBox.TextProperty)?.UpdateTarget();
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
