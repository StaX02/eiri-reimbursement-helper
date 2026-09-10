using Eiri.Reimbursement.Core.DingTalk;

namespace Eiri.Reimbursement.Infrastructure.Sqlite;

public sealed partial class SqliteReimbursementWorkspace : IDingTalkApprovalStatusStore
{
    // Shared by selection and save so a late response cannot update an archived or unsubmitted form.
    private const string ApprovalStatusCandidatePredicate = """
        f.submitted_at IS NOT NULL
        AND (f.exported_at IS NULL OR f.refunded_at IS NULL)
        AND trim(COALESCE(s.instance_id, '')) <> ''
        AND s.pending = 0
        AND (s.status IS NULL OR s.status <> 'COMPLETED' OR s.result IS NULL OR s.result NOT IN ('agree', 'refuse'))
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

    public async Task<bool> SaveApprovalStatusAsync(Guid id, string instanceId, string status, string? result = null, CancellationToken cancellationToken = default)
    {
        if (!new DingTalkApprovalState(status, result).IsValid)
            throw new ArgumentException("审批流程状态或结果无法识别。", nameof(status));
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var sql = connection.CreateCommand();
        sql.CommandText = $"""
            UPDATE dingtalk_approval_submissions AS s SET status = $status, result = $result
            WHERE s.reimbursement_id = $id AND s.instance_id = $instance
            AND EXISTS (SELECT 1 FROM reimbursement_forms f WHERE f.id = s.reimbursement_id
                AND {ApprovalStatusCandidatePredicate});
            """;
        sql.Parameters.AddWithValue("$id", id.ToString());
        sql.Parameters.AddWithValue("$instance", instanceId);
        sql.Parameters.AddWithValue("$status", status);
        sql.Parameters.AddWithValue("$result", status == "COMPLETED" ? (object?)result ?? DBNull.Value : DBNull.Value);
        return await sql.ExecuteNonQueryAsync(cancellationToken) == 1;
    }
}
