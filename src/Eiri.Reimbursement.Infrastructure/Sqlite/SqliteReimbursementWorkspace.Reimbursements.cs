using System.Globalization;
using System.Security.Cryptography;
using Eiri.Reimbursement.Core.Documents;
using Eiri.Reimbursement.Core.Orders;
using Eiri.Reimbursement.Core.Reimbursements;
using Microsoft.Data.Sqlite;

namespace Eiri.Reimbursement.Infrastructure.Sqlite;

public sealed partial class SqliteReimbursementWorkspace : IReimbursementFormWorkspace
{
    public async Task<Guid> CreateReimbursementAsync(IReadOnlyList<OrderId> orderIds, CancellationToken cancellationToken = default)
    {
        OrderId[] ids = orderIds.Distinct().ToArray();
        if (ids.Length == 0) throw new ArgumentException("请至少选择一个订单。", nameof(orderIds));
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction = connection.BeginTransaction();
        Guid id = Guid.NewGuid();
        await using SqliteCommand sql = connection.CreateCommand();
        sql.Transaction = transaction;
        sql.CommandText = "INSERT INTO reimbursement_forms(id, total_minor_units, created_at) VALUES($id, 0, $now);";
        sql.Parameters.AddWithValue("$id", id.ToString());
        sql.Parameters.AddWithValue("$now", Format(DateTimeOffset.UtcNow));
        await sql.ExecuteNonQueryAsync(cancellationToken);
        foreach (OrderId orderId in ids)
        {
            sql.Parameters.Clear();
            sql.CommandText = "UPDATE orders SET reimbursement_id = $id WHERE id = $order AND reimbursement_id IS NULL;";
            sql.Parameters.AddWithValue("$id", id.ToString());
            sql.Parameters.AddWithValue("$order", orderId.ToString());
            if (await sql.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("选中的订单已绑定报销单或已删除，未创建报销单。请刷新后重试。");
        }
        sql.Parameters.Clear();
        sql.CommandText = "UPDATE reimbursement_forms SET total_minor_units = (SELECT COALESCE(SUM(i.total_minor_units), 0) FROM invoices i JOIN orders o ON o.id = i.order_id WHERE o.reimbursement_id = $id) WHERE id = $id;";
        sql.Parameters.AddWithValue("$id", id.ToString());
        await sql.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    public async Task<IReadOnlyList<ReimbursementForm>> ListReimbursementsAsync(int offset = 0, int limit = 100, CancellationToken cancellationToken = default, bool? archived = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (limit is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(limit));
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteCommand sql = connection.CreateCommand();
        sql.CommandText = "SELECT id FROM reimbursement_forms WHERE ($archived IS NULL OR (exported_at IS NOT NULL AND submitted_at IS NOT NULL AND refunded_at IS NOT NULL) = $archived) ORDER BY created_at DESC, id LIMIT $limit OFFSET $offset;";
        sql.Parameters.AddWithValue("$archived", (object?)archived ?? DBNull.Value);
        sql.Parameters.AddWithValue("$limit", limit);
        sql.Parameters.AddWithValue("$offset", offset);
        List<Guid> ids = [];
        await using (SqliteDataReader reader = await sql.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) ids.Add(Guid.Parse(reader.GetString(0)));
        List<ReimbursementForm> forms = [];
        foreach (Guid id in ids)
        {
            ReimbursementDetail? detail = await GetReimbursementAsync(id, cancellationToken);
            if (detail is not null) forms.Add(detail.Form);
        }
        return forms;
    }

    public async Task<ReimbursementDetail?> GetReimbursementAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteCommand sql = connection.CreateCommand();
        sql.Parameters.AddWithValue("$id", id.ToString());
        sql.CommandText = "SELECT application_date, reimbursement_type, content, total_minor_units, exported_at, submitted_at, refunded_at, (SELECT instance_id FROM dingtalk_approval_submissions WHERE reimbursement_id = $id) FROM reimbursement_forms WHERE id = $id;";
        ReimbursementForm form;
        List<OrderId> orders = [];
        await using (SqliteDataReader reader = await sql.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken)) return null;
            form = new(id, reader.IsDBNull(0) ? null : DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetInt64(3), orders,
                ParseNullableTimestamp(reader, 4), ParseNullableTimestamp(reader, 5), ParseNullableTimestamp(reader, 6), reader.IsDBNull(7) ? null : reader.GetString(7));
        }
        sql.CommandText = "SELECT id FROM orders WHERE reimbursement_id = $id ORDER BY created_at, id;";
        await using (SqliteDataReader reader = await sql.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) orders.Add(OrderId.Parse(reader.GetString(0)));
        sql.CommandText = "SELECT id, original_file_name, relative_path, processing_error FROM reimbursement_files WHERE reimbursement_id = $id ORDER BY imported_at, id;";
        List<ReimbursementAttachment> files = [];
        await using (SqliteDataReader reader = await sql.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                files.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), ResolveManagedPath(reader.GetString(2)), reader.IsDBNull(3) ? null : reader.GetString(3)));
        return new(form, files);
    }

    public async Task UpdateReimbursementAsync(UpdateReimbursementCommand command, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteCommand sql = connection.CreateCommand();
        // Manual edits protect all existing values from subsequent automatic replacement.
        sql.CommandText = "UPDATE reimbursement_forms SET application_date = $date, reimbursement_type = $type, content = $content, total_minor_units = $amount, has_analysis = 1 WHERE id = $id;";
        AddFormParameters(sql, command);
        if (await sql.ExecuteNonQueryAsync(cancellationToken) != 1) throw new KeyNotFoundException("报销单已不存在。");
    }

    private static void AddFormParameters(SqliteCommand sql, UpdateReimbursementCommand command)
    {
        sql.Parameters.AddWithValue("$id", command.Id.ToString());
        sql.Parameters.AddWithValue("$date", (object?)command.ApplicationDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? DBNull.Value);
        sql.Parameters.AddWithValue("$type", command.ReimbursementType.Trim());
        sql.Parameters.AddWithValue("$content", command.Content.Trim());
        sql.Parameters.AddWithValue("$amount", (object?)command.TotalMinorUnits ?? DBNull.Value);
    }

    public async Task<ReimbursementImportResult> ImportReimbursementFilesAsync(Guid id, IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        if (await GetReimbursementAsync(id, cancellationToken) is null) throw new KeyNotFoundException("报销单已不存在。");
        int imported = 0;
        List<string> messages = [];
        foreach (string source in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Guid fileId = Guid.NewGuid();
            string name = Path.GetFileName(source);
            string relative = $"originals/reimbursements/{id}/{fileId}{Path.GetExtension(source)}";
            string destination = ResolveManagedPath(relative);
            bool stored = false;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await CopyFileAsync(source, destination, cancellationToken);
                string hash;
                await using (FileStream file = File.OpenRead(destination))
                    hash = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken));
                await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
                await using SqliteCommand sql = connection.CreateCommand();
                sql.CommandText = "INSERT OR IGNORE INTO reimbursement_files(id, reimbursement_id, original_file_name, relative_path, byte_length, sha256, imported_at) VALUES($id, $form, $name, $path, $length, $hash, $now);";
                sql.Parameters.AddWithValue("$id", fileId.ToString());
                sql.Parameters.AddWithValue("$form", id.ToString());
                sql.Parameters.AddWithValue("$name", name);
                sql.Parameters.AddWithValue("$path", relative);
                sql.Parameters.AddWithValue("$length", new FileInfo(destination).Length);
                sql.Parameters.AddWithValue("$hash", hash);
                sql.Parameters.AddWithValue("$now", Format(DateTimeOffset.UtcNow));
                stored = await sql.ExecuteNonQueryAsync(cancellationToken) == 1;
                if (!stored) { messages.Add($"{name}：已附加相同文件。"); continue; }
                imported++;
                if (Path.GetExtension(source).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        if (_documentProcessor is null) throw new InvalidOperationException("文档识别器不可用，请手动填写属性。");
                        DocumentAnalysis analysis = await _documentProcessor.AnalyzeAsync(new(Guid.NewGuid(), destination, DocumentKind.ReimbursementPdf, TimeSpan.FromSeconds(90)), cancellationToken);
                        bool complete = await ApplyReimbursementAnalysisAsync(id, analysis, cancellationToken);
                        if (!complete) throw new InvalidDataException("首页部分字段未识别，请检查并手动补充。");
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                    catch (Exception exception)
                    {
                        sql.Parameters.Clear();
                        sql.CommandText = "UPDATE reimbursement_files SET processing_error = $error WHERE id = $id;";
                        sql.Parameters.AddWithValue("$error", exception.Message);
                        sql.Parameters.AddWithValue("$id", fileId.ToString());
                        await sql.ExecuteNonQueryAsync(cancellationToken);
                        messages.Add($"{name}：附件已保存；{exception.Message}");
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) { messages.Add($"{name}：{exception.Message}"); }
            finally { if (!stored && File.Exists(destination)) File.Delete(destination); }
        }
        return new(imported, messages);
    }

    private async Task<bool> ApplyReimbursementAnalysisAsync(Guid id, DocumentAnalysis analysis, CancellationToken cancellationToken)
    {
        string? Value(string field) => analysis.Candidates
            .Where(c => c.Field == field && c.Page == 1 && c.Confidence >= 0.9 && !string.IsNullOrWhiteSpace(c.Value))
            .OrderByDescending(c => c.Confidence).FirstOrDefault()?.Value.Trim();
        DateOnly? date = DateOnly.TryParseExact(Value("application_date"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsedDate) ? parsedDate : null;
        string? type = Value("reimbursement_type");
        string? content = Value("reimbursement_content");
        long? amount = long.TryParse(Value("total_minor_units"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsedAmount) ? parsedAmount : null;
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteCommand sql = connection.CreateCommand();
        sql.CommandText = """
            UPDATE reimbursement_forms SET
                application_date = CASE WHEN has_analysis = 0 OR application_date IS NULL THEN COALESCE($date, application_date) ELSE application_date END,
                reimbursement_type = CASE WHEN has_analysis = 0 OR reimbursement_type = '' THEN COALESCE($type, reimbursement_type) ELSE reimbursement_type END,
                content = CASE WHEN has_analysis = 0 OR content = '' THEN COALESCE($content, content) ELSE content END,
                total_minor_units = CASE WHEN has_analysis = 0 OR total_minor_units IS NULL THEN COALESCE($amount, total_minor_units) ELSE total_minor_units END,
                has_analysis = CASE WHEN $date IS NOT NULL OR $type IS NOT NULL OR $content IS NOT NULL OR $amount IS NOT NULL THEN 1 ELSE has_analysis END
            WHERE id = $id;
            """;
        sql.Parameters.AddWithValue("$id", id.ToString());
        sql.Parameters.AddWithValue("$date", (object?)date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? DBNull.Value);
        sql.Parameters.AddWithValue("$type", (object?)type ?? DBNull.Value);
        sql.Parameters.AddWithValue("$content", (object?)content ?? DBNull.Value);
        sql.Parameters.AddWithValue("$amount", (object?)amount ?? DBNull.Value);
        await sql.ExecuteNonQueryAsync(cancellationToken);
        return date is not null && type is not null && content is not null && amount is not null;
    }
}
