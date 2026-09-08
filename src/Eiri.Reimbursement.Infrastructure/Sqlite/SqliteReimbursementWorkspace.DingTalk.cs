using Eiri.Reimbursement.Core.DingTalk;
using Microsoft.Data.Sqlite;

namespace Eiri.Reimbursement.Infrastructure.Sqlite;

public sealed partial class SqliteReimbursementWorkspace : IDingTalkConnectionStore
{
    public async Task<DingTalkConnection?> GetDingTalkConnectionAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteCommand sql = connection.CreateCommand();
        sql.CommandText = "SELECT client_id, client_secret, access_token FROM dingtalk_connection WHERE id = 1;";
        await using SqliteDataReader reader = await sql.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(reader.GetString(0), reader.GetString(1), reader.GetString(2)) : null;
    }

    public async Task SaveDingTalkConnectionAsync(DingTalkConnection record, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(record.ClientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.ClientSecret);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.AccessToken);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteCommand sql = connection.CreateCommand();
        sql.CommandText = """
            INSERT INTO dingtalk_connection (id, client_id, client_secret, access_token)
            VALUES (1, $clientId, $clientSecret, $accessToken)
            ON CONFLICT(id) DO UPDATE SET client_id = excluded.client_id,
                client_secret = excluded.client_secret, access_token = excluded.access_token;
            """;
        sql.Parameters.AddWithValue("$clientId", record.ClientId);
        sql.Parameters.AddWithValue("$clientSecret", record.ClientSecret);
        sql.Parameters.AddWithValue("$accessToken", record.AccessToken);
        await sql.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ClearDingTalkConnectionAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteCommand sql = connection.CreateCommand();
        sql.CommandText = "PRAGMA secure_delete = ON; DELETE FROM dingtalk_connection;";
        await sql.ExecuteNonQueryAsync(cancellationToken);
    }
}
