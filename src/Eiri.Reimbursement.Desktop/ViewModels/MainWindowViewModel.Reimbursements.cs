using System.Collections.ObjectModel;
using Eiri.Reimbursement.Core.Orders;
using Eiri.Reimbursement.Core.Reimbursements;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Eiri.Reimbursement.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    private int _reimbursementLoadVersion;
    private int _reimbursementSelectionVersion;
    private readonly Dictionary<Guid, ReimbursementEditorViewModel> _reimbursementEditors = [];
    private readonly Dictionary<Guid, Task<ReimbursementEditorViewModel>> _loadingReimbursementEditors = [];
    private OrderId[] _selectedOrderIds = [];
    public IReadOnlyList<OrderId> SelectedOrderIds => _selectedOrderIds;
    public bool IsUpdatingOrderRows { get; private set; }
    public event Action? OrderRowsUpdated;

    [ObservableProperty]
    private ObservableCollection<ReimbursementListItemViewModel> _reimbursements = [];
    [ObservableProperty]
    private ReimbursementListItemViewModel? _selectedReimbursement;
    [ObservableProperty]
    private ReimbursementEditorViewModel? _reimbursementEditor;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReimbursementSelection))]
    [NotifyPropertyChangedFor(nameof(IsSingleReimbursementSelected))]
    [NotifyPropertyChangedFor(nameof(ReimbursementDetailHeading))]
    [NotifyPropertyChangedFor(nameof(CanExport))]
    private int _selectedReimbursementCount;
    [ObservableProperty]
    private string _reimbursementDetailStatus = "";
    [ObservableProperty]
    private int _reimbursementPage;
    public bool HasReimbursementSelection => SelectedReimbursementCount > 0;
    public bool IsSingleReimbursementSelected => SelectedReimbursementCount == 1;
    public string ReimbursementDetailHeading => SelectedReimbursementCount > 1 ? "已选中多个报销单" : "报销单详情";
    public IReimbursementFormWorkspace? ReimbursementWorkspace => _workspace as IReimbursementFormWorkspace;
    public string ReimbursementPageDisplay => $"第 {ReimbursementPage + 1} 页";

    public async Task SetSelectedReimbursementsAsync(IReadOnlyList<ReimbursementListItemViewModel> selected)
    {
        int version = ++_reimbursementSelectionVersion;
        SelectedReimbursementCount = selected.Count;
        SelectedReimbursement = selected.FirstOrDefault();
        ReimbursementEditor = null;
        ReimbursementDetailStatus = "";
        if (selected.Count > 0)
        {
            SelectedOrder = null;
            SetSelectedOrders([]);
        }
        if (selected.Count != 1 || ReimbursementWorkspace is not { } workspace) return;
        Guid id = selected[0].Id;
        try
        {
            ReimbursementDetailStatus = "正在加载报销单…";
            if (!_reimbursementEditors.TryGetValue(id, out ReimbursementEditorViewModel? editor))
            {
                if (!_loadingReimbursementEditors.TryGetValue(id, out Task<ReimbursementEditorViewModel>? loading))
                {
                    loading = LoadReimbursementEditorAsync(workspace, id);
                    _loadingReimbursementEditors[id] = loading;
                }
                try { editor = await loading; }
                finally { _loadingReimbursementEditors.Remove(id); }
            }
            if (version != _reimbursementSelectionVersion) return;
            ReimbursementEditor = editor;
            ReimbursementDetailStatus = "";
        }
        catch (Exception exception)
        {
            if (version == _reimbursementSelectionVersion) ReimbursementDetailStatus = $"加载报销单失败：{exception.Message}";
        }
    }

    private async Task<ReimbursementEditorViewModel> LoadReimbursementEditorAsync(IReimbursementFormWorkspace workspace, Guid id)
    {
        ReimbursementEditorViewModel editor = new(workspace, id, isReadOnly: IsArchive);
        await editor.LoadAsync();
        editor.Saved += OnReimbursementSaved;
        editor.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(editor.IsBusy)) OnPropertyChanged(nameof(CanManageData)); };
        _reimbursementEditors[id] = editor;
        return editor;
    }

    private void OnReimbursementSaved(ReimbursementForm form)
    {
        ReimbursementListItemViewModel? row = Reimbursements.FirstOrDefault(item => item.Id == form.Id);
        if (row is not null) row.Form = form;
        OrderId? activeOrderId = SelectedOrder?.Id;
        IsUpdatingOrderRows = true;
        try
        {
            for (int i = 0; i < Orders.Count; i++)
                if (Orders[i].ReimbursementId == form.Id)
                    Orders[i] = Orders[i] with { ReimbursementContent = form.Content };
            if (activeOrderId is not null) SelectedOrder = Orders.FirstOrDefault(order => order.Id == activeOrderId);
            OrderRowsUpdated?.Invoke();
            SelectedOrderCount = _selectedOrderIds.Length;
        }
        finally { IsUpdatingOrderRows = false; }
    }

    internal async Task RefreshAfterDingTalkApprovalAsync(ReimbursementEditorViewModel editor)
    {
        await editor.LoadAsync();
        await ReloadOrdersAsync(SelectedOrder?.Id);
    }

    public async Task ReloadReimbursementsAsync(int? page = null)
    {
        if (ReimbursementWorkspace is not { } workspace) return;
        int version = ++_reimbursementLoadVersion;
        int target = Math.Max(0, page ?? ReimbursementPage);
        IReadOnlyList<ReimbursementForm> forms = await workspace.ListReimbursementsAsync(target * 100, archived: IsArchive);
        while (forms.Count == 0 && target > 0)
        {
            target--;
            forms = await workspace.ListReimbursementsAsync(target * 100, archived: IsArchive);
        }
        if (version != _reimbursementLoadVersion) return;
        ReimbursementPage = target;
        var ids = forms.Select(form => form.Id).ToHashSet();
        if (SelectedReimbursement is { } selected && !ids.Contains(selected.Id))
            await SetSelectedReimbursementsAsync([]);
        for (int i = Reimbursements.Count - 1; i >= 0; i--)
            if (!ids.Contains(Reimbursements[i].Id)) Reimbursements.RemoveAt(i);
        for (int i = 0; i < forms.Count; i++)
        {
            if (_reimbursementEditors.TryGetValue(forms[i].Id, out var editor)) editor.RefreshOrderSummary(forms[i]);
            ReimbursementListItemViewModel? row = Reimbursements.FirstOrDefault(item => item.Id == forms[i].Id);
            if (row is null) Reimbursements.Insert(i, new(forms[i]));
            else
            {
                row.Form = forms[i];
                int oldIndex = Reimbursements.IndexOf(row);
                if (oldIndex != i) Reimbursements.Move(oldIndex, i);
            }
        }
        OnPropertyChanged(nameof(ReimbursementPageDisplay));
    }

    public bool HasPendingReimbursementWrites => _reimbursementEditors.Values.Any(editor => !editor.PendingSave.IsCompleted || !editor.PendingImport.IsCompleted || editor.HasChanges);

    public async Task FlushReimbursementChangesAsync()
    {
        await Task.WhenAll(_reimbursementEditors.Values.Select(editor => editor.PendingImport));
        bool[] saved = await Task.WhenAll(_reimbursementEditors.Values.Select(editor => editor.PendingSave));
        if (saved.Any(success => !success)) throw new InvalidOperationException("报销单修改尚未保存，请检查日期、金额或自动保存错误。");
    }

    private void ClearReimbursementEditors()
    {
        ++_reimbursementSelectionVersion;
        foreach (var editor in _reimbursementEditors.Values) editor.Saved -= OnReimbursementSaved;
        _reimbursementEditors.Clear();
        _loadingReimbursementEditors.Clear();
        SelectedReimbursementCount = 0;
        SelectedReimbursement = null;
        ReimbursementEditor = null;
        Reimbursements.Clear();
    }

    public async Task<Guid?> CreateReimbursementAsync(IReadOnlyList<OrderId> ids)
    {
        if (IsArchive || IsBusy || ids.Count == 0 || ReimbursementWorkspace is not { } workspace) return null;
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
