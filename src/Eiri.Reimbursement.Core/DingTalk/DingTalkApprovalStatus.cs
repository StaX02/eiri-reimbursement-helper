namespace Eiri.Reimbursement.Core.DingTalk;

public interface IDingTalkApprovalStatusClient
{
    Task<string> GetInstanceStatusAsync(string accessToken, string instanceId, CancellationToken cancellationToken = default);
}

public sealed record DingTalkApprovalStatusCandidate(Guid Id, string InstanceId, string Content);

public interface IDingTalkApprovalStatusStore
{
    Task<IReadOnlyList<DingTalkApprovalStatusCandidate>> ListApprovalStatusCandidatesAsync(CancellationToken cancellationToken = default);
    Task<bool> SaveApprovalStatusAsync(Guid id, string instanceId, string status, CancellationToken cancellationToken = default);
}
