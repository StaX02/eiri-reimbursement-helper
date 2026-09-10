using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Eiri.Reimbursement.Core.Reimbursements;

namespace Eiri.Reimbursement.Desktop.ViewModels;

public partial class ReimbursementEditorViewModel(IReimbursementFormWorkspace workspace, Guid id, bool isReadOnly = false) : ObservableObject
{
    public Guid Id => id;
    public string DingTalkInstanceId => _original?.DingTalkInstanceId ?? "";
    public string ApprovalStatusDisplay => _original?.ApprovalStatusDisplay ?? "未提交";
    public bool IsExported => _original?.ExportedAt is not null;
    public bool IsSubmitted => _original?.SubmittedAt is not null;
    public bool IsRefunded => _original?.RefundedAt is not null;
    public string ExportedDisplay => StatusDisplay(_original?.ExportedAt);
    public string SubmittedDisplay => StatusDisplay(_original?.SubmittedAt);
    public string RefundedDisplay => StatusDisplay(_original?.RefundedAt);
    private static string StatusDisplay(DateTimeOffset? time) => time?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "暂未设置";
    private void NotifyMilestones()
    {
        OnPropertyChanged(nameof(DingTalkInstanceId));
        OnPropertyChanged(nameof(ApprovalStatusDisplay));
        foreach (string property in new[] { nameof(IsExported), nameof(IsSubmitted), nameof(IsRefunded), nameof(ExportedDisplay), nameof(SubmittedDisplay), nameof(RefundedDisplay) }) OnPropertyChanged(property);
    }
    private ReimbursementForm? _original;
    private bool _loading;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    public event Action<ReimbursementForm>? Saved;
    public Task<bool> PendingSave { get; private set; } = Task.FromResult(true);
    public Task PendingImport { get; private set; } = Task.CompletedTask;

    [ObservableProperty] private string _applicationDate = "";
    [ObservableProperty] private string _reimbursementType = "";
    [ObservableProperty] private string _content = "";
    [ObservableProperty] private string _totalAmount = "";
    [ObservableProperty] private string _orderSummary = "";
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private ObservableCollection<ReimbursementAttachment> _attachments = [];
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(IsReadOnly))]
    private bool _isBusy;
    public bool IsReadOnly => isReadOnly || IsBusy;
    public string AttachmentsEmptyHint => isReadOnly ? "暂无附件。" : "暂无附件，将文件拖入上方区域。";
    public bool IsEditable => !isReadOnly;
    public bool CanEdit => !isReadOnly && !IsBusy;
    public bool HasChanges => !isReadOnly && _original is not null &&
        (!TryGetUpdate(out UpdateReimbursementCommand? update, out _) || !MatchesOriginal(update!));

    partial void OnApplicationDateChanged(string value) => QueueSave();
    partial void OnReimbursementTypeChanged(string value) => QueueSave();
    partial void OnContentChanged(string value) => QueueSave();
    partial void OnTotalAmountChanged(string value) => QueueSave();

    private void QueueSave()
    {
        if (!isReadOnly && !_loading && _original is not null) PendingSave = SaveAsync();
    }

    public async Task LoadAsync()
    {
        ReimbursementDetail detail = await workspace.GetReimbursementAsync(id) ?? throw new InvalidOperationException("报销单已不存在。");
        _loading = true;
        try
        {
            _original = detail.Form;
            NotifyMilestones();
            ApplicationDate = _original.ApplicationDate?.ToString("yyyy-MM-dd") ?? "";
            ReimbursementType = _original.ReimbursementType;
            Content = _original.Content;
            TotalAmount = _original.TotalAmount?.ToString("0.00", CultureInfo.InvariantCulture) ?? "";
            OrderSummary = $"已绑定 {_original.OrderIds.Count} 个订单\n" + string.Join("\n", _original.OrderIds);
            Attachments = new(detail.Attachments);
        }
        finally { _loading = false; }
    }

    private bool TryGetUpdate(out UpdateReimbursementCommand? update, out string error)
    {
        update = null;
        error = "";
        DateOnly? date = null;
        if (!string.IsNullOrWhiteSpace(ApplicationDate))
        {
            if (!DateOnly.TryParseExact(ApplicationDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsed))
            { error = "申请日期请填写为 yyyy-MM-dd，例如 2026-08-25。"; return false; }
            date = parsed;
        }
        long? amount = null;
        if (!string.IsNullOrWhiteSpace(TotalAmount))
        {
            if (!decimal.TryParse(TotalAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed) ||
                parsed > long.MaxValue / 100m || parsed < long.MinValue / 100m || parsed * 100 != decimal.Truncate(parsed * 100))
            { error = "总金额请输入最多两位小数的有效金额。"; return false; }
            amount = (long)(parsed * 100);
        }
        update = new(id, date, ReimbursementType.Trim(), Content.Trim(), amount);
        return true;
    }

    private bool MatchesOriginal(UpdateReimbursementCommand update) => _original is { } original &&
        original.ApplicationDate == update.ApplicationDate && original.ReimbursementType == update.ReimbursementType &&
        original.Content == update.Content && original.TotalMinorUnits == update.TotalMinorUnits;

    public async Task<bool> SaveAsync()
    {
        if (isReadOnly) return true;
        await _saveLock.WaitAsync();
        try
        {
            if (_original is null) return true;
            if (!TryGetUpdate(out UpdateReimbursementCommand? update, out string error))
            { StatusMessage = error; return false; }
            if (MatchesOriginal(update!)) { StatusMessage = "修改已自动保存。"; return true; }
            StatusMessage = "正在自动保存…";
            await workspace.UpdateReimbursementAsync(update!);
            _original = _original with
            {
                ApplicationDate = update!.ApplicationDate,
                ReimbursementType = update.ReimbursementType,
                Content = update.Content,
                TotalMinorUnits = update.TotalMinorUnits,
            };
            // Keep the user's current input and caret intact while queued edits are saved.
            Saved?.Invoke(_original);
            StatusMessage = "修改已自动保存。";
            return true;
        }
        catch (Exception exception) { StatusMessage = $"自动保存失败：{exception.Message}。请修改字段后重试。"; return false; }
        finally { _saveLock.Release(); }
    }

    public void RefreshOrderSummary(ReimbursementForm form)
    {
        if (_original is not null) _original = _original with { OrderIds = form.OrderIds, ExportedAt = form.ExportedAt, SubmittedAt = form.SubmittedAt, RefundedAt = form.RefundedAt, DingTalkInstanceId = form.DingTalkInstanceId, DingTalkApprovalStatus = form.DingTalkApprovalStatus };
        NotifyMilestones();
        OrderSummary = $"已绑定 {form.OrderIds.Count} 个订单\n" + string.Join("\n", form.OrderIds);
    }

    public Task ImportAsync(IReadOnlyList<string> paths)
    {
        if (!CanEdit || paths.Count == 0) return Task.CompletedTask;
        return PendingImport = ImportCoreAsync(paths);
    }

    private async Task ImportCoreAsync(IReadOnlyList<string> paths)
    {
        IsBusy = true;
        try
        {
            if (!await SaveAsync()) return;
            StatusMessage = "正在保存附件并识别 PDF 首页…";
            ReimbursementImportResult result = await workspace.ImportReimbursementFilesAsync(id, paths);
            await LoadAsync();
            Saved?.Invoke(_original!);
            StatusMessage = $"已添加 {result.ImportedCount} 个附件。" + string.Join("\n", result.Messages);
        }
        catch (Exception exception) { StatusMessage = $"添加附件失败：{exception.Message}"; }
        finally { IsBusy = false; }
    }
}
