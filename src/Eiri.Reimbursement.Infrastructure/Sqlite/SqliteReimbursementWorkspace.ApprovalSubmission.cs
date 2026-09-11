using Eiri.Reimbursement.Core.DingTalk;

namespace Eiri.Reimbursement.Infrastructure.Sqlite;

public sealed partial class SqliteReimbursementWorkspace : IDingTalkApprovalStore
{
    public async Task<DingTalkApprovalSubmission?> GetApprovalSubmissionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var sql = connection.CreateCommand();
        sql.CommandText = "SELECT CASE WHEN pending = 1 THEN NULL ELSE instance_id END FROM dingtalk_approval_submissions WHERE reimbursement_id = $id;";
        sql.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await sql.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new(reader.IsDBNull(0) ? null : reader.GetString(0)) : null;
    }

    public async Task<bool> BeginApprovalSubmissionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var sql = connection.CreateCommand();
        sql.CommandText = """
            INSERT INTO dingtalk_approval_submissions(reimbursement_id, created_at, pending)
            SELECT id, $now, 1 FROM reimbursement_forms WHERE id = $id
                AND (exported_at IS NULL OR submitted_at IS NULL OR refunded_at IS NULL)
                AND (submitted_at IS NULL OR EXISTS (
                    SELECT 1 FROM dingtalk_approval_submissions WHERE reimbursement_id = $id
                    AND (status = 'TERMINATED' OR (status = 'COMPLETED' AND result = 'refuse'))))
            ON CONFLICT(reimbursement_id) DO UPDATE SET pending = 1, created_at = $now
                WHERE pending = 0 AND instance_id IS NOT NULL
                AND (status = 'TERMINATED' OR (status = 'COMPLETED' AND result = 'refuse'));
            """;
        sql.Parameters.AddWithValue("$id", id.ToString());
        sql.Parameters.AddWithValue("$now", Format(DateTimeOffset.UtcNow));
        return await sql.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task CompleteApprovalSubmissionAsync(Guid id, string instanceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        await using var sql = connection.CreateCommand();
        sql.Transaction = transaction;
        sql.CommandText = "UPDATE dingtalk_approval_submissions SET instance_id = $instance, status = NULL, result = NULL, business_id = NULL, pending = 0 WHERE reimbursement_id = $id AND (pending = 1 OR instance_id IS NULL);";
        sql.Parameters.AddWithValue("$id", id.ToString());
        sql.Parameters.AddWithValue("$instance", instanceId);
        if (await sql.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException("无法保存审批实例记录。");
        sql.Parameters.AddWithValue("$now", Format(DateTimeOffset.UtcNow));
        sql.CommandText = """
            UPDATE reimbursement_forms SET submitted_at = $now WHERE id = $id;
            UPDATE orders SET submitted_at = $now, updated_at = $now WHERE reimbursement_id = $id;
            """;
        await sql.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ClearPendingApprovalSubmissionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var sql = connection.CreateCommand();
        sql.CommandText = """
            BEGIN;
            DELETE FROM dingtalk_approval_submissions WHERE reimbursement_id = $id AND instance_id IS NULL;
            UPDATE dingtalk_approval_submissions SET pending = 0 WHERE reimbursement_id = $id AND pending = 1;
            COMMIT;
            """;
        sql.Parameters.AddWithValue("$id", id.ToString());
        await sql.ExecuteNonQueryAsync(cancellationToken);
    }
}
