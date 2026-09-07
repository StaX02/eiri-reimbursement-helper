using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Eiri.Reimbursement.Core.Reimbursements;

namespace Eiri.Reimbursement.Desktop.ViewModels;

public partial class ReimbursementEditorViewModel(IReimbursementFormWorkspace workspace, Guid id) : ObservableObject
{
    private ReimbursementForm? _original;
    [ObservableProperty] private string _applicationDate = "";
    [ObservableProperty] private string _reimbursementType = "";
    [ObservableProperty] private string _content = "";
    [ObservableProperty] private string _totalAmount = "";
    [ObservableProperty] private string _orderSummary = "";
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private ObservableCollection<ReimbursementAttachment> _attachments = [];
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    private bool _isBusy;
    public bool CanEdit => !IsBusy;
    public bool HasChanges => _original is not null &&
        (ApplicationDate != (_original.ApplicationDate?.ToString("yyyy-MM-dd") ?? "") ||
         ReimbursementType != _original.ReimbursementType || Content != _original.Content ||
         TotalAmount != (_original.TotalAmount?.ToString("0.00", CultureInfo.InvariantCulture) ?? ""));

    public async Task LoadAsync()
    {
        ReimbursementDetail detail = await workspace.GetReimbursementAsync(id) ?? throw new InvalidOperationException("报销单已不存在。");
        _original = detail.Form;
        ApplicationDate = _original.ApplicationDate?.ToString("yyyy-MM-dd") ?? "";
        ReimbursementType = _original.ReimbursementType;
        Content = _original.Content;
        TotalAmount = _original.TotalAmount?.ToString("0.00", CultureInfo.InvariantCulture) ?? "";
        OrderSummary = $"已绑定 {_original.OrderIds.Count} 个订单\n" + string.Join("\n", _original.OrderIds);
        Attachments = new(detail.Attachments);
    }

    public async Task<bool> SaveAsync()
    {
        if (IsBusy) return false;
        if (!HasChanges) return true;
        DateOnly? date = null;
        if (!string.IsNullOrWhiteSpace(ApplicationDate))
        {
            if (!DateOnly.TryParseExact(ApplicationDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsed))
            { StatusMessage = "申请日期请填写为 yyyy-MM-dd，例如 2026-08-25。"; return false; }
            date = parsed;
        }
        long? amount = null;
        if (!string.IsNullOrWhiteSpace(TotalAmount))
        {
            if (!decimal.TryParse(TotalAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed) ||
                parsed > long.MaxValue / 100m || parsed < long.MinValue / 100m || parsed * 100 != decimal.Truncate(parsed * 100))
            { StatusMessage = "总金额请输入最多两位小数的有效金额。"; return false; }
            amount = (long)(parsed * 100);
        }
        IsBusy = true;
        try
        {
            await workspace.UpdateReimbursementAsync(new(id, date, ReimbursementType, Content, amount));
            await LoadAsync();
            StatusMessage = "报销单已保存。";
            return true;
        }
        catch (Exception exception) { StatusMessage = $"保存失败：{exception.Message}"; return false; }
        finally { IsBusy = false; }
    }

    public async Task ImportAsync(IReadOnlyList<string> paths)
    {
        if (IsBusy || paths.Count == 0 || !await SaveAsync()) return;
        IsBusy = true;
        StatusMessage = "正在保存附件并识别 PDF 首页…";
        try
        {
            ReimbursementImportResult result = await workspace.ImportReimbursementFilesAsync(id, paths);
            await LoadAsync();
            StatusMessage = $"已添加 {result.ImportedCount} 个附件。" + string.Join("\n", result.Messages);
        }
        catch (Exception exception) { StatusMessage = $"添加附件失败：{exception.Message}"; }
        finally { IsBusy = false; }
    }
}
