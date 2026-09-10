using System.Text.Json;

namespace Eiri.Reimbursement.Core.DingTalk;

public interface IDingTalkApprovalClient
{
    Task<string> CreateInstanceAsync(string accessToken, JsonElement request, CancellationToken cancellationToken = default);
}

public sealed class DingTalkApprovalRejectedException(string message) : InvalidOperationException(message);

public sealed record DingTalkApprovalSubmission(string? InstanceId);

public interface IDingTalkApprovalStore
{
    Task<DingTalkApprovalSubmission?> GetApprovalSubmissionAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> BeginApprovalSubmissionAsync(Guid id, CancellationToken cancellationToken = default);
    Task CompleteApprovalSubmissionAsync(Guid id, string instanceId, CancellationToken cancellationToken = default);
    Task ClearPendingApprovalSubmissionAsync(Guid id, CancellationToken cancellationToken = default);
}
