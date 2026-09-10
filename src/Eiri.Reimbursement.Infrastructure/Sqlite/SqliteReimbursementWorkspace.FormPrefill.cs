using System.Text.Json;
using Eiri.Reimbursement.Core.DingTalk;
using Microsoft.Data.Sqlite;

namespace Eiri.Reimbursement.Infrastructure.Sqlite;

public sealed partial class SqliteReimbursementWorkspace : IDingTalkFormPrefillStore
{
    public async Task<IReadOnlyDictionary<string, DingTalkPrefillValue>> GetFormPrefillAsync(string processCode, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var sql = connection.CreateCommand();
        sql.CommandText = "SELECT values_json FROM dingtalk_form_prefill WHERE connection_id=1 AND process_code=$code;";
        sql.Parameters.AddWithValue("$code", processCode);
        var result = await sql.ExecuteScalarAsync(cancellationToken);
        return result is string json ? JsonSerializer.Deserialize<Dictionary<string, DingTalkPrefillValue>>(json)! : new Dictionary<string, DingTalkPrefillValue>();
    }

    public async Task SaveFormPrefillAsync(string processCode, IReadOnlyDictionary<string, DingTalkPrefillValue> values, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processCode);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var sql = connection.CreateCommand();
        sql.CommandText = """
            INSERT INTO dingtalk_form_prefill (connection_id,process_code,values_json) VALUES (1,$code,$values)
            ON CONFLICT(connection_id,process_code) DO UPDATE SET values_json=excluded.values_json;
            """;
        sql.Parameters.AddWithValue("$code", processCode);
        sql.Parameters.AddWithValue("$values", JsonSerializer.Serialize(values));
        await sql.ExecuteNonQueryAsync(cancellationToken);
    }
}
