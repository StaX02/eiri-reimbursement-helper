using Eiri.Reimbursement.Core.DingTalk;
using Microsoft.Data.Sqlite;

namespace Eiri.Reimbursement.Infrastructure.Sqlite;

public sealed partial class SqliteReimbursementWorkspace : IDingTalkSubmissionInfoStore
{
    public async Task<DingTalkSubmissionInfo> GetSubmissionInfoAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteCommand sql = connection.CreateCommand();
        sql.CommandText = "SELECT dept_id, department_name, user_id, user_name FROM dingtalk_submission_info WHERE id = 1;";
        await using SqliteDataReader reader = await sql.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(new(reader.GetInt64(0), reader.GetString(1)), reader.IsDBNull(2) ? null : new(reader.GetString(2), reader.GetString(3)))
            : new(null, null);
    }

    public async Task SaveSubmissionInfoAsync(DingTalkSubmissionInfo info, CancellationToken cancellationToken = default)
    {
        if (info.Department is null && info.User is not null) throw new ArgumentException("选择报销人前必须选择部门。", nameof(info));
        if (info.Department is not null)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(info.Department.DeptId, 1);
            ArgumentException.ThrowIfNullOrWhiteSpace(info.Department.Name);
        }
        if (info.User is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(info.User.UserId);
            ArgumentException.ThrowIfNullOrWhiteSpace(info.User.Name);
        }
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteCommand sql = connection.CreateCommand();
        if (info.Department is null) sql.CommandText = "DELETE FROM dingtalk_submission_info;";
        else
        {
            sql.CommandText = """
                INSERT INTO dingtalk_submission_info (id, dept_id, department_name, user_id, user_name)
                VALUES (1, $department, $departmentName, $user, $userName)
                ON CONFLICT(id) DO UPDATE SET dept_id = excluded.dept_id, department_name = excluded.department_name,
                    user_id = excluded.user_id, user_name = excluded.user_name;
                """;
            sql.Parameters.AddWithValue("$department", info.Department.DeptId);
            sql.Parameters.AddWithValue("$departmentName", info.Department.Name);
            sql.Parameters.AddWithValue("$user", (object?)info.User?.UserId ?? DBNull.Value);
            sql.Parameters.AddWithValue("$userName", (object?)info.User?.Name ?? DBNull.Value);
        }
        await sql.ExecuteNonQueryAsync(cancellationToken);
    }
}
