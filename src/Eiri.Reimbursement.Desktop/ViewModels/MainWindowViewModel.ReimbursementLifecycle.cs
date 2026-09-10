using Eiri.Reimbursement.Core.Export;
using Eiri.Reimbursement.Core.Orders;
using Eiri.Reimbursement.Core.Reimbursements;

namespace Eiri.Reimbursement.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    public Task UnarchiveReimbursementsAsync(IReadOnlyList<Guid> ids) =>
        !IsArchive || ids.Count == 0 ? Task.CompletedTask : RunReimbursementActionAsync(
            workspace => workspace.SetReimbursementMilestonesAsync(ids.Distinct()
                .Select(id => new SetReimbursementMilestoneCommand(id, Milestone.Refunded, null)).ToArray()),
            $"已取消归档 {ids.Distinct().Count()} 个报销单，报销单及关联订单已恢复至主窗口并设为未返款。", allowArchive: true);

    public Task SetReimbursementsMilestoneAsync(IReadOnlyList<Guid> ids, Milestone milestone, bool value) =>
        RunReimbursementActionAsync(async workspace =>
        {
            DateTimeOffset? time = value ? DateTimeOffset.UtcNow : null;
            await workspace.SetReimbursementMilestonesAsync(ids.Distinct().Select(id => new SetReimbursementMilestoneCommand(id, milestone, time)).ToArray());
        }, "已同步报销单及关联订单状态。");

    public Task ClearReimbursementsStatusesAsync(IReadOnlyList<Guid> ids) =>
        RunReimbursementActionAsync(workspace => workspace.SetReimbursementMilestonesAsync(ids.Distinct().SelectMany(id => new[] {
            new SetReimbursementMilestoneCommand(id, Milestone.Submitted, null),
            new SetReimbursementMilestoneCommand(id, Milestone.Refunded, null) }).ToArray()), "已清除报销单及关联订单的提交、返款状态。");

    public Task ExportReimbursementsAsync(IReadOnlyList<Guid> ids, string destinationDirectory) =>
        RunReimbursementActionAsync(async workspace =>
        {
            if (_batchExporter is null) throw new InvalidOperationException("导出功能不可用。");
            await _batchExporter.ExportAsync(new ExportBatchCommand([], destinationDirectory, ids));
            var time = DateTimeOffset.UtcNow;
            await workspace.SetReimbursementMilestonesAsync(ids.Distinct().Select(id => new SetReimbursementMilestoneCommand(id, Milestone.Exported, time)).ToArray());
        }, "已导出报销单首页及关联订单资料，并同步已导出状态。");

    public Task DeleteReimbursementsAsync(IReadOnlyList<Guid> ids) =>
        RunReimbursementActionAsync(async workspace =>
        {
            await workspace.DeleteReimbursementsAsync(ids);
            foreach (Guid id in ids)
                if (_reimbursementEditors.Remove(id, out var editor)) editor.Saved -= OnReimbursementSaved;
            await SetSelectedReimbursementsAsync([]);
        }, "已删除报销单及其附件，关联订单已解绑。", discardDraftIds: ids);

    private async Task RunReimbursementActionAsync(Func<IReimbursementFormWorkspace, Task> action, string success, IReadOnlyList<Guid>? discardDraftIds = null, bool allowArchive = false)
    {
        if ((IsArchive && !allowArchive) || IsBusy || ReimbursementWorkspace is not { } workspace) return;
        IsBusy = true;
        try
        {
            await Task.WhenAll(_loadingReimbursementEditors.Values.ToArray());
            if (discardDraftIds is null) await FlushReimbursementChangesAsync();
            else
            {
                var editors = _reimbursementEditors.Where(pair => discardDraftIds.Contains(pair.Key)).Select(pair => pair.Value).ToArray();
                await Task.WhenAll(editors.Select(editor => editor.PendingImport));
                await Task.WhenAll(editors.Select(editor => editor.PendingSave));
            }
            await action(workspace);
            await ReloadOrdersAsync(SelectedOrder?.Id);
            StatusMessage = success;
        }
        catch (Exception exception) { StatusMessage = $"操作失败：{exception.Message}"; }
        finally { IsBusy = false; }
    }
}
