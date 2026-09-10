using Eiri.Reimbursement.Core.DingTalk;

namespace Eiri.Reimbursement.Infrastructure.Sqlite;

public sealed partial class SqliteReimbursementWorkspace : IDingTalkApprovalStatusStore
{
    // Shared by selection and save so a late response cannot update an archived or unsubmitted form.
    private const string ApprovalStatusCandidatePredicate = """
        f.submitted_at IS NOT NULL
        AND (f.exported_at IS NULL OR f.refunded_at IS NULL)
        AND trim(COALESCE(s.instance_id, '')) <> ''
        AND (s.status IS NULL OR s.status <> 'COMPLETED')
        """;

    public async Task<IReadOnlyList<DingTalkApprovalStatusCandidate>> ListApprovalStatusCandidatesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var sql = connection.CreateCommand();
        sql.CommandText = $"""
            SELECT f.id, s.instance_id, f.content FROM reimbursement_forms f
            JOIN dingtalk_approval_submissions s ON s.reimbursement_id = f.id
            WHERE {ApprovalStatusCandidatePredicate} ORDER BY f.created_at, f.id;
            """;
        List<DingTalkApprovalStatusCandidate> candidates = [];
        await using var reader = await sql.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            candidates.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2)));
        return candidates;
    }

    public async Task<bool> SaveApprovalStatusAsync(Guid id, string instanceId, string status, CancellationToken cancellationToken = default)
    {
        if (status is not ("RUNNING" or "TERMINATED" or "COMPLETED"))
            throw new ArgumentException("审批流程状态无法识别。", nameof(status));
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var sql = connection.CreateCommand();
        sql.CommandText = $"""
            UPDATE dingtalk_approval_submissions AS s SET status = $status
            WHERE s.reimbursement_id = $id AND s.instance_id = $instance
            AND EXISTS (SELECT 1 FROM reimbursement_forms f WHERE f.id = s.reimbursement_id
                AND {ApprovalStatusCandidatePredicate});
            """;
        sql.Parameters.AddWithValue("$id", id.ToString());
        sql.Parameters.AddWithValue("$instance", instanceId);
        sql.Parameters.AddWithValue("$status", status);
        return await sql.ExecuteNonQueryAsync(cancellationToken) == 1;
    }
}
