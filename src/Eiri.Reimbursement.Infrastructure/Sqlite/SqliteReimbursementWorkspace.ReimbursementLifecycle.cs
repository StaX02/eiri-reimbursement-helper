using Eiri.Reimbursement.Core.Orders;
using Eiri.Reimbursement.Core.Reimbursements;
using Microsoft.Data.Sqlite;

namespace Eiri.Reimbursement.Infrastructure.Sqlite;

public sealed partial class SqliteReimbursementWorkspace
{
    public async Task SetReimbursementMilestonesAsync(IReadOnlyList<SetReimbursementMilestoneCommand> commands, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction = connection.BeginTransaction();
        foreach (var command in commands)
        {
            string column = command.Milestone switch
            {
                Milestone.Exported => "exported_at",
                Milestone.Submitted => "submitted_at",
                Milestone.Refunded => "refunded_at",
                _ => throw new ArgumentOutOfRangeException(nameof(commands)),
            };
            await using SqliteCommand sql = connection.CreateCommand();
            sql.Transaction = transaction;
            sql.CommandText = $"UPDATE reimbursement_forms SET {column} = $time WHERE id = $id;";
            sql.Parameters.AddWithValue("$id", command.Id.ToString());
            sql.Parameters.AddWithValue("$time", command.OccurredAt is { } time ? Format(time) : DBNull.Value);
            if (await sql.ExecuteNonQueryAsync(cancellationToken) != 1) throw new KeyNotFoundException("报销单已不存在，状态未修改。");
            sql.CommandText = $"UPDATE orders SET {column} = $time, updated_at = $now WHERE reimbursement_id = $id;";
            sql.Parameters.AddWithValue("$now", Format(DateTimeOffset.UtcNow));
            await sql.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task RecoverReimbursementDeletionsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        string root = ResolveManagedPath("staging/reimbursements-deleting");
        if (!Directory.Exists(root)) return;
        foreach (string directory in Directory.EnumerateDirectories(root))
        {
            if (!Guid.TryParse(Path.GetFileName(directory), out Guid id)) continue;
            await using var sql = connection.CreateCommand();
            sql.CommandText = "SELECT COUNT(*) FROM reimbursement_forms WHERE id = $id;";
            sql.Parameters.AddWithValue("$id", id.ToString());
            if (Convert.ToInt64(await sql.ExecuteScalarAsync(cancellationToken)) == 0) TryDeleteDirectory(directory);
            else
            {
                string original = ResolveManagedPath($"originals/reimbursements/{id}");
                if (Directory.Exists(original)) throw new IOException("报销单附件恢复目录已存在，请检查资料库。");
                Directory.CreateDirectory(Path.GetDirectoryName(original)!);
                Directory.Move(directory, original);
            }
        }
    }

    public async Task DeleteReimbursementsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default)
    {
        List<(string Original, string Staged)> directories = [];
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction = connection.BeginTransaction();
        try
        {
            foreach (Guid id in ids.Distinct())
            {
                await using SqliteCommand sql = connection.CreateCommand();
                sql.Transaction = transaction;
                sql.Parameters.AddWithValue("$id", id.ToString());
                sql.CommandText = "SELECT COUNT(*) FROM reimbursement_forms WHERE id = $id;";
                if (Convert.ToInt64(await sql.ExecuteScalarAsync(cancellationToken)) != 1) throw new KeyNotFoundException("报销单已不存在，删除未完成。");
                string original = ResolveManagedPath($"originals/reimbursements/{id}");
                string staged = ResolveManagedPath($"staging/reimbursements-deleting/{id}");
                if (Directory.Exists(original))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
                    Directory.Move(original, staged);
                    directories.Add((original, staged));
                }
                sql.CommandText = "UPDATE orders SET reimbursement_id = NULL WHERE reimbursement_id = $id; DELETE FROM reimbursement_files WHERE reimbursement_id = $id; DELETE FROM reimbursement_forms WHERE id = $id;";
                await sql.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            foreach (var directory in directories)
                if (Directory.Exists(directory.Staged)) Directory.Move(directory.Staged, directory.Original);
            throw;
        }
        foreach (var directory in directories) TryDeleteDirectory(directory.Staged);
    }
}
