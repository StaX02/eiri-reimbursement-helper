using Eiri.Reimbursement.Core.Orders;

namespace Eiri.Reimbursement.Core.Reimbursements;

public sealed record ReimbursementForm(
    Guid Id,
    DateOnly? ApplicationDate,
    string ReimbursementType,
    string Content,
    long? TotalMinorUnits,
    IReadOnlyList<OrderId> OrderIds,
    DateTimeOffset? ExportedAt = null,
    DateTimeOffset? SubmittedAt = null,
    DateTimeOffset? RefundedAt = null,
    string? DingTalkInstanceId = null)
{
    public string ApplicationDateDisplay => ApplicationDate?.ToString("yyyy-MM-dd") ?? "待填写";
    public decimal? TotalAmount => TotalMinorUnits / 100m;
    public string ContentDisplay => string.IsNullOrWhiteSpace(Content) ? "报销内容待填写" : Content;
}

public sealed record ReimbursementAttachment(Guid Id, string OriginalFileName, string ManagedPath, string? ProcessingError);
public sealed record ReimbursementDetail(ReimbursementForm Form, IReadOnlyList<ReimbursementAttachment> Attachments);
public sealed record UpdateReimbursementCommand(Guid Id, DateOnly? ApplicationDate, string ReimbursementType, string Content, long? TotalMinorUnits);
public sealed record SetReimbursementMilestoneCommand(Guid Id, Milestone Milestone, DateTimeOffset? OccurredAt);

public sealed record ReimbursementImportResult(int ImportedCount, IReadOnlyList<string> Messages);

public interface IReimbursementFormWorkspace
{
    Task SetReimbursementMilestonesAsync(IReadOnlyList<SetReimbursementMilestoneCommand> commands, CancellationToken cancellationToken = default);
    Task DeleteReimbursementsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default);
    Task<Guid> CreateReimbursementAsync(IReadOnlyList<OrderId> orderIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReimbursementForm>> ListReimbursementsAsync(int offset = 0, int limit = 100, CancellationToken cancellationToken = default, bool? archived = null);
    Task<ReimbursementDetail?> GetReimbursementAsync(Guid id, CancellationToken cancellationToken = default);
    Task UpdateReimbursementAsync(UpdateReimbursementCommand command, CancellationToken cancellationToken = default);
    Task<ReimbursementImportResult> ImportReimbursementFilesAsync(Guid id, IReadOnlyList<string> paths, CancellationToken cancellationToken = default);
}
