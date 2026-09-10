using CommunityToolkit.Mvvm.Input;
using Eiri.Reimbursement.Core.DingTalk;

namespace Eiri.Reimbursement.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    private bool CanRefreshApprovalStatuses() => !IsArchive && !IsBusy && _approvalStatusClient is not null;

    [RelayCommand(CanExecute = nameof(CanRefreshApprovalStatuses))]
    public async Task RefreshApprovalStatusesAsync(CancellationToken cancellationToken = default)
    {
        if (!CanRefreshApprovalStatuses()) return;
        IsBusy = true;
        StatusMessage = "正在刷新审批流程…";
        try
        {
            if (_workspace is not IDingTalkApprovalStatusStore store || _workspace is not IDingTalkConnectionStore connectionStore)
                throw new InvalidOperationException("审批流程服务不可用，请重启完整安装的软件。");
            var candidates = await store.ListApprovalStatusCandidatesAsync(cancellationToken);
            if (candidates.Count == 0) { StatusMessage = "没有需要刷新流程的报销单。"; return; }
            var connection = await connectionStore.GetDingTalkConnectionAsync(cancellationToken);
            if (connection is null || string.IsNullOrWhiteSpace(connection.AccessToken))
                throw new InvalidOperationException("请先通过“钉钉 → 连接接口”连接应用，再刷新流程。");
            if (connection.ExpiresAt is not { } expiry || expiry <= DateTimeOffset.UtcNow)
                throw new InvalidOperationException("钉钉连接已过期，请通过“钉钉 → 连接接口”更新连接，再刷新流程。");

            int refreshed = 0, skipped = 0;
            List<string> errors = [];
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                StatusMessage = $"正在刷新审批流程（{refreshed + skipped + errors.Count + 1}/{candidates.Count}）…";
                try
                {
                    var state = await _approvalStatusClient!.GetInstanceStatusAsync(connection.AccessToken, candidate.InstanceId, cancellationToken);
                    if (await store.SaveApprovalStatusAsync(candidate.Id, candidate.InstanceId, state.Status, state.Result, cancellationToken)) refreshed++;
                    else skipped++;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception exception)
                {
                    string reason = exception switch
                    {
                        OperationCanceledException => "请求超时，请稍后重试。",
                        System.Net.Http.HttpRequestException => "网络连接失败，请检查网络后重试。",
                        InvalidOperationException => exception.Message,
                        _ => "获取或保存失败，请检查钉钉连接及资料库访问权限后重试。",
                    };
                    string label = string.IsNullOrWhiteSpace(candidate.Content) ? candidate.Id.ToString() : candidate.Content;
                    errors.Add($"{label}：{reason}");
                }
            }
            // Update rows and milestone/status snapshots without reloading editable fields.
            await ReloadReimbursementsAsync();
            StatusMessage = $"流程刷新结束：成功 {refreshed} 个，失败 {errors.Count} 个，跳过 {skipped} 个。"
                + (errors.Count == 0 ? "" : "失败条目保留上次状态。\n" + string.Join("\n", errors));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { StatusMessage = "流程刷新已取消，已保存的状态将保留。"; }
        catch (InvalidOperationException exception) { StatusMessage = $"刷新流程失败：{exception.Message}"; }
        catch (Exception) { StatusMessage = "刷新流程失败，请检查资料库访问权限后重试。"; }
        finally { IsBusy = false; }
    }
}
