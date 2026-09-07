using System.Collections.ObjectModel;
using Eiri.Reimbursement.Core.Orders;
using Eiri.Reimbursement.Core.Reimbursements;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Eiri.Reimbursement.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    private int _reimbursementLoadVersion;

    [ObservableProperty]
    private ObservableCollection<ReimbursementForm> _reimbursements = [];
    [ObservableProperty]
    private ReimbursementForm? _selectedReimbursement;
    [ObservableProperty]
    private int _reimbursementPage;
    public IReimbursementFormWorkspace? ReimbursementWorkspace => _workspace as IReimbursementFormWorkspace;
    public string ReimbursementPageDisplay => $"第 {ReimbursementPage + 1} 页";

    public async Task ReloadReimbursementsAsync(int? page = null)
    {
        if (ReimbursementWorkspace is not { } workspace) return;
        int version = ++_reimbursementLoadVersion;
        Guid? selectedId = SelectedReimbursement?.Id;
        int target = Math.Max(0, page ?? ReimbursementPage);
        IReadOnlyList<ReimbursementForm> forms = await workspace.ListReimbursementsAsync(target * 100);
        while (forms.Count == 0 && target > 0)
        {
            target--;
            forms = await workspace.ListReimbursementsAsync(target * 100);
        }
        if (version != _reimbursementLoadVersion) return;
        ReimbursementPage = target;
        Reimbursements = new(forms);
        SelectedReimbursement = Reimbursements.FirstOrDefault(form => form.Id == selectedId);
        OnPropertyChanged(nameof(ReimbursementPageDisplay));
    }

    public async Task<Guid?> CreateReimbursementAsync(IReadOnlyList<OrderId> ids)
    {
        if (IsBusy || ids.Count == 0 || ReimbursementWorkspace is not { } workspace) return null;
        IsBusy = true;
        try
        {
            Guid id = await workspace.CreateReimbursementAsync(ids);
            await ReloadOrdersAsync(SelectedOrder?.Id);
            await ReloadReimbursementsAsync(0);
            StatusMessage = $"已创建报销单，绑定 {ids.Distinct().Count()} 个订单。";
            return id;
        }
        catch (Exception exception) { StatusMessage = exception.Message; return null; }
        finally { IsBusy = false; }
    }
}
