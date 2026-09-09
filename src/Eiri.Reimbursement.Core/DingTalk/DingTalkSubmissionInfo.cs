namespace Eiri.Reimbursement.Core.DingTalk;

public sealed record DingTalkDepartment(long DeptId, string Name);
public sealed record DingTalkUser(string UserId, string Name);
public sealed record DingTalkSubmissionInfo(DingTalkDepartment? Department, DingTalkUser? User);

public interface IDingTalkDirectoryClient
{
    Task<IReadOnlyList<DingTalkDepartment>> GetDepartmentsAsync(string accessToken, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DingTalkUser>> GetUsersAsync(string accessToken, long deptId, CancellationToken cancellationToken = default);
}

public interface IDingTalkSubmissionInfoStore
{
    Task<DingTalkSubmissionInfo> GetSubmissionInfoAsync(CancellationToken cancellationToken = default);
    Task SaveSubmissionInfoAsync(DingTalkSubmissionInfo info, CancellationToken cancellationToken = default);
}
