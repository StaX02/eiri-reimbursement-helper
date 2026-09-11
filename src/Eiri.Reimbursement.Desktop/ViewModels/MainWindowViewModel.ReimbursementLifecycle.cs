using System.IO;
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

    public Task<bool> ExportApprovedReimbursementsAsync(IReadOnlyList<Guid> ids, string destinationDirectory) =>
        RunReimbursementActionAsync(async workspace =>
        {
            if (_approvedExporter is null) throw new InvalidOperationException("PDF 导出功能不可用，请使用包含文档处理程序的完整安装包。");
            int succeeded = 0;
            List<string> errors = [];
            foreach (var id in ids.Distinct())
            {
                var form = (await workspace.GetReimbursementAsync(id))?.Form;
                string label = form?.ContentDisplay ?? id.ToString();
                string name = $"报销审批-{form?.DingTalkBusinessId ?? id.ToString("N")}-{label}";
                foreach (char invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
                name = name[..Math.Min(name.Length, 100)].TrimEnd(' ', '.');
                string path = Path.Combine(destinationDirectory, name + ".pdf");
                for (int suffix = 2; File.Exists(path) || Directory.Exists(path); suffix++)
                    path = Path.Combine(destinationDirectory, $"{name}-{suffix}.pdf");
                try
                {
                    await _approvedExporter.ExportAsync(id, path);
                    succeeded++;
                    await MarkPdfExportedAsync(workspace, id);
                }
                catch (Exception exception) { errors.Add($"{label}：{exception.Message}"); }
            }
            if (errors.Count > 0)
            {
                await ReloadOrdersAsync(SelectedOrder?.Id);
                throw new InvalidOperationException($"审批 PDF 已生成 {succeeded} 份，{errors.Count} 份处理出现问题。\n" + string.Join("\n", errors));
            }
        }, $"已导出 {ids.Distinct().Count()} 份审批 PDF，并同步已导出状态。");

    public Task<bool> ExportApprovedReimbursementAsync(Guid id, string destinationPath) =>
        RunReimbursementActionAsync(async workspace =>
        {
            if (_approvedExporter is null) throw new InvalidOperationException("PDF 导出功能不可用，请使用包含文档处理程序的完整安装包。");
            await _approvedExporter.ExportAsync(id, destinationPath, overwrite: true);
            await MarkPdfExportedAsync(workspace, id);
        }, "已导出审批 PDF，并同步报销单及关联订单的已导出状态。");

    private static async Task MarkPdfExportedAsync(IReimbursementFormWorkspace workspace, Guid id)
    {
        try { await workspace.SetReimbursementMilestonesAsync([new SetReimbursementMilestoneCommand(id, Milestone.Exported, DateTimeOffset.UtcNow)]); }
        catch (Exception exception) { throw new InvalidOperationException("PDF 文件已生成、状态未更新，请手动设置已导出。", exception); }
    }

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

    private async Task<bool> RunReimbursementActionAsync(Func<IReimbursementFormWorkspace, Task> action, string success, IReadOnlyList<Guid>? discardDraftIds = null, bool allowArchive = false)
    {
        if ((IsArchive && !allowArchive) || IsBusy || ReimbursementWorkspace is not { } workspace) return false;
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
            return true;
        }
        catch (Exception exception) { StatusMessage = $"操作失败：{exception.Message}"; return false; }
        finally { IsBusy = false; }
    }
}
