namespace Eiri.Reimbursement.Core.DingTalk;

public interface IDingTalkApprovalStatusClient
{
    Task<DingTalkApprovalState> GetInstanceStatusAsync(string accessToken, string instanceId, CancellationToken cancellationToken = default);
}

public sealed record DingTalkApprovalState(string Status, string? Result = null, string? BusinessId = null)
{
    public bool IsValid => Status is "RUNNING" or "TERMINATED" || Status == "COMPLETED" && Result is "agree" or "refuse";
    public bool AllowsResubmission => Status == "TERMINATED" || Status == "COMPLETED" && Result == "refuse";
    public string DisplayName => Status switch
    {
        "RUNNING" => "审批中",
        "TERMINATED" => "已撤销",
        "COMPLETED" => Result switch { "agree" => "已同意", "refuse" => "已拒绝", _ => "结果待获取" },
        _ => "待获取",
    };
}

public sealed record DingTalkApprovalStatusCandidate(Guid Id, string InstanceId, string Content);

public interface IDingTalkApprovalStatusStore
{
    Task<IReadOnlyList<DingTalkApprovalStatusCandidate>> ListApprovalStatusCandidatesAsync(CancellationToken cancellationToken = default);
    Task<bool> SaveApprovalStatusAsync(Guid id, string instanceId, string status, string? result = null, CancellationToken cancellationToken = default, string? businessId = null);
}
