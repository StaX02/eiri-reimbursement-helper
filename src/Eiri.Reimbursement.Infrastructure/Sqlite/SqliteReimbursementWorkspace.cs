using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Eiri.Reimbursement.Core;
using Eiri.Reimbursement.Core.Documents;
using Eiri.Reimbursement.Core.Invoices;
using Eiri.Reimbursement.Core.Materials;
using Eiri.Reimbursement.Core.Orders;
using Microsoft.Data.Sqlite;

namespace Eiri.Reimbursement.Infrastructure.Sqlite;

public sealed class SqliteReimbursementWorkspace(
    string libraryRoot,
    IDocumentProcessor? documentProcessor = null) : IReimbursementWorkspace
{
    private const char AggregateSeparator = '\u001F';
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _libraryRoot = Path.GetFullPath(libraryRoot);
    private readonly IDocumentProcessor? _documentProcessor = documentProcessor;
    private string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = Path.Combine(_libraryRoot, "library.db"),
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared,
        ForeignKeys = true,
        Pooling = false,
    }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_libraryRoot);
        Directory.CreateDirectory(Path.Combine(_libraryRoot, "originals"));
        Directory.CreateDirectory(Path.Combine(_libraryRoot, "cache"));
        Directory.CreateDirectory(Path.Combine(_libraryRoot, "staging"));
        Directory.CreateDirectory(Path.Combine(_libraryRoot, "logs"));
        CleanupStagedDeletions();

        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await ApplyMigrationsAsync(connection, cancellationToken);
    }

    public async Task<OrderId> CreateOrderAsync(
        CreateOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        OrderId id = OrderId.New();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteCommand sql = connection.CreateCommand();
        sql.CommandText =
            """
            INSERT INTO orders (
                id, platform, external_order_number, notes, created_at, updated_at)
            VALUES (
                $id, $platform, $externalOrderNumber, $notes, $createdAt, $updatedAt);
            """;
        sql.Parameters.AddWithValue("$id", id.ToString());
        sql.Parameters.AddWithValue("$platform", command.Platform.ToString());
        sql.Parameters.AddWithValue("$externalOrderNumber", (object?)Normalize(command.ExternalOrderNumber) ?? DBNull.Value);
        sql.Parameters.AddWithValue("$notes", (object?)Normalize(command.Notes) ?? DBNull.Value);
        sql.Parameters.AddWithValue("$createdAt", Format(now));
        sql.Parameters.AddWithValue("$updatedAt", Format(now));
        await sql.ExecuteNonQueryAsync(cancellationToken);

        return id;
    }

    public async Task<IReadOnlyList<OrderListItem>> SearchOrdersAsync(
        OrderQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(query.Offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.Limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(query.Limit, 500);

        List<OrderListItem> orders = [];
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteCommand sql = connection.CreateCommand();
        sql.CommandText =
            """
            SELECT
                o.id,
                o.platform,
                o.external_order_number,
                COALESCE((
                    SELECT group_concat(merchant_name, char(31))
                    FROM (SELECT DISTINCT merchant_name FROM invoices WHERE order_id = o.id AND merchant_name <> '')
                ), ''),
                COALESCE((
                    SELECT
                        (SELECT first_line.name
                         FROM invoice_lines first_line
                         WHERE first_line.invoice_id = i.id
                           AND first_line.is_effective = 1
                           AND first_line.name <> ''
                         ORDER BY first_line.sequence
                         LIMIT 1)
                        || CASE
                            WHEN (SELECT COUNT(*) FROM invoices WHERE order_id = o.id) > 1
                            THEN '等'
                            WHEN (SELECT COUNT(*)
                                  FROM invoice_lines counted_line
                                  WHERE counted_line.invoice_id = i.id
                                    AND counted_line.is_effective = 1
                                    AND counted_line.name <> '') > 1
                            THEN '等' || ((SELECT COUNT(*)
                                             FROM invoice_lines counted_line
                                             WHERE counted_line.invoice_id = i.id
                                               AND counted_line.is_effective = 1
                                               AND counted_line.name <> '') - 1) || '条'
                            ELSE ''
                           END
                    FROM invoices i
                    JOIN managed_files invoice_file ON invoice_file.id = i.managed_file_id
                    WHERE i.order_id = o.id
                    ORDER BY invoice_file.imported_at, invoice_file.rowid
                    LIMIT 1
                ), ''),
                COALESCE((SELECT SUM(total_minor_units) FROM invoices WHERE order_id = o.id), 0),
                COALESCE((
                    SELECT group_concat(invoice_number, char(31))
                    FROM (
                        SELECT CASE WHEN invoice_number = '' THEN '待提取' ELSE invoice_number END AS invoice_number
                        FROM invoices
                        WHERE order_id = o.id
                    )
                ), ''),
                (SELECT COUNT(*) FROM invoices WHERE order_id = o.id),
                o.exported_at,
                o.submitted_at,
                o.refunded_at,
                o.created_at
            FROM orders o
            WHERE ($platform IS NULL OR o.platform = $platform)
              AND ($searchText IS NULL
                   OR o.external_order_number LIKE '%' || $searchText || '%'
                   OR EXISTS (
                       SELECT 1 FROM invoices i
                       WHERE i.order_id = o.id
                         AND (i.merchant_name LIKE '%' || $searchText || '%'
                              OR i.invoice_number LIKE '%' || $searchText || '%')))
            ORDER BY o.created_at DESC
            LIMIT $limit OFFSET $offset;
            """;
        sql.Parameters.AddWithValue("$platform", query.Platform is null ? DBNull.Value : query.Platform.Value.ToString());
        sql.Parameters.AddWithValue("$searchText", (object?)Normalize(query.SearchText) ?? DBNull.Value);
        sql.Parameters.AddWithValue("$limit", query.Limit);
        sql.Parameters.AddWithValue("$offset", query.Offset);

        await using SqliteDataReader reader = await sql.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            orders.Add(new OrderListItem(
                OrderId.Parse(reader.GetString(0)),
                Enum.Parse<OrderPlatform>(reader.GetString(1)),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                SplitAggregate(reader.GetString(3)),
                SplitAggregate(reader.GetString(4)),
                reader.GetInt64(5),
                SplitAggregate(reader.GetString(6)),
                reader.GetInt32(7),
                ParseNullableTimestamp(reader, 8),
                ParseNullableTimestamp(reader, 9),
                ParseNullableTimestamp(reader, 10),
                DateTimeOffset.Parse(reader.GetString(11), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        }

        return orders;
    }

    public async Task SetMilestoneAsync(
        SetMilestoneCommand command,
        CancellationToken cancellationToken = default)
    {
        string column = command.Milestone switch
        {
            Milestone.Exported => "exported_at",
            Milestone.Submitted => "submitted_at",
            Milestone.Refunded => "refunded_at",
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };

        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteCommand sql = connection.CreateCommand();
        sql.CommandText = $"UPDATE orders SET {column} = $occurredAt, updated_at = $updatedAt WHERE id = $id;";
        sql.Parameters.AddWithValue("$occurredAt", command.OccurredAt is null ? DBNull.Value : Format(command.OccurredAt.Value));
        sql.Parameters.AddWithValue("$updatedAt", Format(DateTimeOffset.UtcNow));
        sql.Parameters.AddWithValue("$id", command.OrderId.ToString());

        int changed = await sql.ExecuteNonQueryAsync(cancellationToken);
        if (changed == 0)
        {
            throw new KeyNotFoundException($"Order '{command.OrderId}' was not found.");
        }
    }

    public async Task UpdateOrderPlatformAsync(
        UpdateOrderPlatformCommand command,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteCommand sql = connection.CreateCommand();
        sql.CommandText =
            """
            UPDATE orders
            SET platform = $platform, updated_at = $updatedAt
            WHERE id = $id;
            """;
        sql.Parameters.AddWithValue("$platform", command.Platform.ToString());
        sql.Parameters.AddWithValue("$updatedAt", Format(DateTimeOffset.UtcNow));
        sql.Parameters.AddWithValue("$id", command.OrderId.ToString());
        int changed = await sql.ExecuteNonQueryAsync(cancellationToken);
        if (changed == 0)
        {
            throw new KeyNotFoundException($"Order '{command.OrderId}' was not found.");
        }
    }

    public async Task<ImportMaterialsResult> ImportMaterialsAsync(
        ImportMaterialsCommand command,
        CancellationToken cancellationToken = default)
    {
        List<MaterialImportItem> results = [];
        int analysisFailureCount = 0;
        foreach (string sourcePath in command.SourcePaths)
        {
            MaterialImportItem item = await ImportMaterialAsync(
                command.OrderId,
                sourcePath,
                command.Role,
                cancellationToken);
            results.Add(item);
            if (command.Role == ManagedFileRole.InvoicePdf
                && item is { Outcome: MaterialImportOutcome.Imported, Material: not null })
            {
                try
                {
                    InvoiceId invoiceId = await GetInvoiceIdAsync(item.Material.Id, cancellationToken);
                    await AnalyzeInvoiceAsync(invoiceId, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    analysisFailureCount++;
                }
            }
        }

        return new ImportMaterialsResult(results, analysisFailureCount);
    }

    private async Task<InvoiceId> GetInvoiceIdAsync(
        ManagedFileId managedFileId,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteCommand sql = connection.CreateCommand();
        sql.CommandText = "SELECT id FROM invoices WHERE managed_file_id = $managedFileId;";
        sql.Parameters.AddWithValue("$managedFileId", managedFileId.ToString());
        string? invoiceId = await sql.ExecuteScalarAsync(cancellationToken) as string;
        return invoiceId is null
            ? throw new KeyNotFoundException($"Invoice for material '{managedFileId}' was not found.")
            : InvoiceId.Parse(invoiceId);
    }

    public async Task<OrderDetail?> GetOrderAsync(
        OrderId orderId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteCommand orderSql = connection.CreateCommand();
        orderSql.CommandText =
            """
            SELECT platform, external_order_number, notes
            FROM orders
            WHERE id = $id;
            """;
        orderSql.Parameters.AddWithValue("$id", orderId.ToString());

        OrderPlatform platform;
        string? externalOrderNumber;
        string? notes;
        await using (SqliteDataReader reader = await orderSql.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            platform = Enum.Parse<OrderPlatform>(reader.GetString(0));
            externalOrderNumber = reader.IsDBNull(1) ? null : reader.GetString(1);
            notes = reader.IsDBNull(2) ? null : reader.GetString(2);
        }

        List<ManagedMaterial> materials = [];
        await using SqliteCommand materialSql = connection.CreateCommand();
        materialSql.CommandText =
            """
            SELECT id, role, original_file_name, relative_path, media_type,
                   byte_length, sha256, processing_state, imported_at
            FROM managed_files
            WHERE order_id = $orderId
            ORDER BY imported_at, id;
            """;
        materialSql.Parameters.AddWithValue("$orderId", orderId.ToString());

        await using SqliteDataReader materialReader = await materialSql.ExecuteReaderAsync(cancellationToken);
        while (await materialReader.ReadAsync(cancellationToken))
        {
            string relativePath = materialReader.GetString(3);
            materials.Add(new ManagedMaterial(
                ManagedFileId.Parse(materialReader.GetString(0)),
                Enum.Parse<ManagedFileRole>(materialReader.GetString(1)),
                materialReader.GetString(2),
                ResolveManagedPath(relativePath),
                materialReader.GetString(4),
                materialReader.GetInt64(5),
                materialReader.GetString(6),
                Enum.Parse<MaterialProcessingState>(materialReader.GetString(7)),
                DateTimeOffset.Parse(
                    materialReader.GetString(8),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind)));
        }

        List<InvoiceRow> invoiceRows = [];
        await using (SqliteCommand invoiceSql = connection.CreateCommand())
        {
            invoiceSql.CommandText =
                """
                SELECT i.id, i.managed_file_id, mf.original_file_name,
                       i.merchant_name, i.invoice_number, i.total_minor_units, i.needs_review
                FROM invoices i
                JOIN managed_files mf ON mf.id = i.managed_file_id
                WHERE i.order_id = $orderId
                ORDER BY mf.imported_at, mf.rowid;
                """;
            invoiceSql.Parameters.AddWithValue("$orderId", orderId.ToString());

            await using SqliteDataReader invoiceReader = await invoiceSql.ExecuteReaderAsync(cancellationToken);
            while (await invoiceReader.ReadAsync(cancellationToken))
            {
                invoiceRows.Add(new InvoiceRow(
                    InvoiceId.Parse(invoiceReader.GetString(0)),
                    ManagedFileId.Parse(invoiceReader.GetString(1)),
                    invoiceReader.GetString(2),
                    invoiceReader.GetString(3),
                    invoiceReader.GetString(4),
                    invoiceReader.GetInt64(5),
                    invoiceReader.GetBoolean(6)));
            }
        }

        List<InvoiceDetail> invoices = [];
        foreach (InvoiceRow invoice in invoiceRows)
        {
            List<InvoiceLineDetail> lines = [];
            await using SqliteCommand lineSql = connection.CreateCommand();
            lineSql.CommandText =
                """
                SELECT sequence, name, amount_minor_units, is_effective
                FROM invoice_lines
                WHERE invoice_id = $invoiceId
                ORDER BY sequence;
                """;
            lineSql.Parameters.AddWithValue("$invoiceId", invoice.Id.ToString());
            await using SqliteDataReader lineReader = await lineSql.ExecuteReaderAsync(cancellationToken);
            while (await lineReader.ReadAsync(cancellationToken))
            {
                lines.Add(new InvoiceLineDetail(
                    lineReader.GetInt32(0),
                    lineReader.GetString(1),
                    lineReader.IsDBNull(2) ? null : lineReader.GetInt64(2),
                    lineReader.GetBoolean(3)));
            }

            invoices.Add(new InvoiceDetail(
                invoice.Id,
                invoice.ManagedFileId,
                invoice.OriginalFileName,
                invoice.MerchantName,
                invoice.InvoiceNumber,
                invoice.TotalMinorUnits,
                invoice.NeedsReview,
                lines));
        }

        return new OrderDetail(orderId, platform, externalOrderNumber, notes, materials, invoices);
    }

    public async Task UpdateInvoiceAsync(
        UpdateInvoiceCommand command,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (SqliteCommand updateSql = connection.CreateCommand())
        {
            updateSql.Transaction = transaction;
            updateSql.CommandText =
                """
                UPDATE invoices
                SET merchant_name = $merchantName,
                    invoice_number = $invoiceNumber,
                    total_minor_units = $totalMinorUnits,
                    needs_review = 0,
                    is_user_corrected = 1,
                    updated_at = $updatedAt
                WHERE id = $id;
                """;
            updateSql.Parameters.AddWithValue("$merchantName", Normalize(command.MerchantName) ?? string.Empty);
            updateSql.Parameters.AddWithValue("$invoiceNumber", Normalize(command.InvoiceNumber) ?? string.Empty);
            updateSql.Parameters.AddWithValue("$totalMinorUnits", command.TotalMinorUnits);
            updateSql.Parameters.AddWithValue("$updatedAt", Format(DateTimeOffset.UtcNow));
            updateSql.Parameters.AddWithValue("$id", command.InvoiceId.ToString());
            int changed = await updateSql.ExecuteNonQueryAsync(cancellationToken);
            if (changed == 0)
            {
                throw new KeyNotFoundException($"Invoice '{command.InvoiceId}' was not found.");
            }
        }

        await using (SqliteCommand deleteLinesSql = connection.CreateCommand())
        {
            deleteLinesSql.Transaction = transaction;
            deleteLinesSql.CommandText = "DELETE FROM invoice_lines WHERE invoice_id = $invoiceId;";
            deleteLinesSql.Parameters.AddWithValue("$invoiceId", command.InvoiceId.ToString());
            await deleteLinesSql.ExecuteNonQueryAsync(cancellationToken);
        }

        int sequence = 0;
        foreach (InvoiceLineCorrection line in command.Lines)
        {
            string? name = Normalize(line.Name);
            if (name is null)
            {
                continue;
            }

            await using SqliteCommand insertLineSql = connection.CreateCommand();
            insertLineSql.Transaction = transaction;
            insertLineSql.CommandText =
                """
                INSERT INTO invoice_lines (
                    id, invoice_id, sequence, name, amount_minor_units, is_effective)
                VALUES (
                    $id, $invoiceId, $sequence, $name, $amountMinorUnits, $isEffective);
                """;
            insertLineSql.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
            insertLineSql.Parameters.AddWithValue("$invoiceId", command.InvoiceId.ToString());
            insertLineSql.Parameters.AddWithValue("$sequence", sequence++);
            insertLineSql.Parameters.AddWithValue("$name", name);
            insertLineSql.Parameters.AddWithValue(
                "$amountMinorUnits",
                line.AmountMinorUnits is null ? DBNull.Value : line.AmountMinorUnits.Value);
            insertLineSql.Parameters.AddWithValue("$isEffective", line.IsEffective ? 1 : 0);
            await insertLineSql.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteOrderAsync(
        OrderId orderId,
        CancellationToken cancellationToken = default)
    {
        string orderDirectory = ResolveManagedPath(Path.Combine("originals", "orders", orderId.ToString()));
        string deletingRoot = ResolveManagedPath(Path.Combine("staging", "deleting"));
        string stagedDirectory = Path.Combine(deletingRoot, $"{orderId}-{Guid.NewGuid():N}");
        bool directoryStaged = false;

        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            if (!await OrderExistsAsync(connection, transaction, orderId, cancellationToken))
            {
                throw new KeyNotFoundException($"Order '{orderId}' was not found.");
            }

            if (Directory.Exists(orderDirectory))
            {
                Directory.CreateDirectory(deletingRoot);
                Directory.Move(orderDirectory, stagedDirectory);
                directoryStaged = true;
            }

            await ExecuteDeleteAsync(
                connection,
                transaction,
                "DELETE FROM invoices WHERE order_id = $orderId;",
                orderId,
                cancellationToken);
            await ExecuteDeleteAsync(
                connection,
                transaction,
                "DELETE FROM managed_files WHERE order_id = $orderId;",
                orderId,
                cancellationToken);
            await ExecuteDeleteAsync(
                connection,
                transaction,
                "DELETE FROM orders WHERE id = $orderId;",
                orderId,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            if (directoryStaged && Directory.Exists(stagedDirectory) && !Directory.Exists(orderDirectory))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(orderDirectory)!);
                Directory.Move(stagedDirectory, orderDirectory);
            }

            throw;
        }

        if (directoryStaged)
        {
            TryDeleteDirectory(stagedDirectory);
        }
    }

    public async Task<DocumentAnalysis> AnalyzeInvoiceAsync(
        InvoiceId invoiceId,
        CancellationToken cancellationToken = default)
    {
        if (_documentProcessor is null)
        {
            throw new InvalidOperationException("Document worker is not configured.");
        }

        ManagedFileId managedFileId;
        string managedPath;
        await using (SqliteConnection connection = await OpenConnectionAsync(cancellationToken))
        await using (SqliteCommand sql = connection.CreateCommand())
        {
            sql.CommandText =
                """
                SELECT mf.id, mf.relative_path
                FROM invoices i
                JOIN managed_files mf ON mf.id = i.managed_file_id
                WHERE i.id = $invoiceId;
                """;
            sql.Parameters.AddWithValue("$invoiceId", invoiceId.ToString());
            await using SqliteDataReader reader = await sql.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new KeyNotFoundException($"Invoice '{invoiceId}' was not found.");
            }

            managedFileId = ManagedFileId.Parse(reader.GetString(0));
            managedPath = ResolveManagedPath(reader.GetString(1));
        }

        await UpdateProcessingStateAsync(
            managedFileId,
            MaterialProcessingState.Processing,
            null,
            cancellationToken);
        try
        {
            DocumentAnalysis analysis = await _documentProcessor.AnalyzeAsync(
                new DocumentJob(
                    Guid.NewGuid(),
                    managedPath,
                    DocumentKind.InvoicePdf,
                    TimeSpan.FromSeconds(30)),
                cancellationToken);
            await SaveAnalysisAsync(managedFileId, analysis, cancellationToken);
            return analysis;
        }
        catch (Exception exception)
        {
            await UpdateProcessingStateAsync(
                managedFileId,
                MaterialProcessingState.Failed,
                exception.Message,
                CancellationToken.None);
            throw;
        }
    }

    private async Task<MaterialImportItem> ImportMaterialAsync(
        OrderId orderId,
        string sourcePath,
        ManagedFileRole role,
        CancellationToken cancellationToken)
    {
        string fullSourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSourcePath))
        {
            return Rejected(sourcePath, "文件不存在或无法访问。");
        }

        MaterialType? materialType = ClassifyMaterial(fullSourcePath, role);
        if (materialType is null)
        {
            return Rejected(sourcePath, role == ManagedFileRole.InvoicePdf
                ? "发票仅支持 PDF 文件。"
                : "辅助材料支持 PDF、PNG、JPG、JPEG 文件。");
        }

        if (!await HasExpectedSignatureAsync(fullSourcePath, materialType, cancellationToken))
        {
            return Rejected(sourcePath, "文件内容与扩展名不匹配或文件已损坏。");
        }

        ManagedFileId fileId = ManagedFileId.New();
        string stagingPath = Path.Combine(_libraryRoot, "staging", $"{fileId}.tmp");
        string relativePath = Path.Combine(
            "originals",
            "orders",
            orderId.ToString(),
            materialType.FolderName,
            $"{fileId}{materialType.Extension}");
        string destinationPath = ResolveManagedPath(relativePath);
        bool destinationCreated = false;

        try
        {
            await CopyFileAsync(fullSourcePath, stagingPath, cancellationToken);
            FileInfo stagedFile = new(stagingPath);
            string sha256 = await ComputeSha256Async(stagingPath, cancellationToken);
            DateTimeOffset importedAt = DateTimeOffset.UtcNow;

            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteTransaction transaction =
                (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

            if (!await OrderExistsAsync(connection, transaction, orderId, cancellationToken))
            {
                throw new KeyNotFoundException($"Order '{orderId}' was not found.");
            }

            string? duplicateFileName = await FindDuplicateFileNameAsync(
                connection,
                transaction,
                sha256,
                cancellationToken);
            if (duplicateFileName is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new MaterialImportItem(
                    sourcePath,
                    MaterialImportOutcome.Duplicate,
                    null,
                    $"与已导入文件“{duplicateFileName}”内容相同。");
            }

            await InsertManagedFileAsync(
                connection,
                transaction,
                fileId,
                orderId,
                materialType,
                Path.GetFileName(fullSourcePath),
                relativePath,
                stagedFile.Length,
                sha256,
                importedAt,
                cancellationToken);
            if (materialType.Role == ManagedFileRole.InvoicePdf)
            {
                await InsertInvoicePlaceholderAsync(
                    connection,
                    transaction,
                    fileId,
                    orderId,
                    importedAt,
                    cancellationToken);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Move(stagingPath, destinationPath);
            destinationCreated = true;
            await transaction.CommitAsync(cancellationToken);

            ManagedMaterial material = new(
                fileId,
                materialType.Role,
                Path.GetFileName(fullSourcePath),
                destinationPath,
                materialType.MediaType,
                stagedFile.Length,
                sha256,
                materialType.ProcessingState,
                importedAt);
            return new MaterialImportItem(sourcePath, MaterialImportOutcome.Imported, material, null);
        }
        catch
        {
            if (destinationCreated)
            {
                File.Delete(destinationPath);
            }

            throw;
        }
        finally
        {
            if (File.Exists(stagingPath))
            {
                File.Delete(stagingPath);
            }
        }
    }

    private async Task ApplyMigrationsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand versionSql = connection.CreateCommand();
        versionSql.CommandText = "PRAGMA user_version;";
        int version = Convert.ToInt32(await versionSql.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);

        if (version > Schema.CurrentVersion)
        {
            throw new InvalidOperationException(
                $"Database schema version {version} is newer than supported version {Schema.CurrentVersion}.");
        }

        if (version == 0)
        {
            await ExecuteNonQueryAsync(connection, Schema.Version1, cancellationToken);
            version = 1;
        }

        if (version == 1)
        {
            await ExecuteNonQueryAsync(connection, Schema.Version2, cancellationToken);
            version = 2;
        }

        if (version == 2)
        {
            await ExecuteNonQueryAsync(connection, Schema.Version3, cancellationToken);
        }
    }

    private static async Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using FileStream source = new(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81_920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using FileStream destination = new(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81_920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(destination, cancellationToken);
    }

    private static async Task<string> ComputeSha256Async(
        string filePath,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(filePath);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    private static async Task<bool> OrderExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        OrderId orderId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand sql = connection.CreateCommand();
        sql.Transaction = transaction;
        sql.CommandText = "SELECT EXISTS(SELECT 1 FROM orders WHERE id = $id);";
        sql.Parameters.AddWithValue("$id", orderId.ToString());
        return Convert.ToInt32(await sql.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) == 1;
    }

    private static async Task ExecuteDeleteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string commandText,
        OrderId orderId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand sql = connection.CreateCommand();
        sql.Transaction = transaction;
        sql.CommandText = commandText;
        sql.Parameters.AddWithValue("$orderId", orderId.ToString());
        await sql.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task UpdateProcessingStateAsync(
        ManagedFileId managedFileId,
        MaterialProcessingState state,
        string? error,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteCommand sql = connection.CreateCommand();
        sql.CommandText =
            """
            UPDATE managed_files
            SET processing_state = $state, processing_error = $error
            WHERE id = $id;
            """;
        sql.Parameters.AddWithValue("$state", state.ToString());
        sql.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
        sql.Parameters.AddWithValue("$id", managedFileId.ToString());
        await sql.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task SaveAnalysisAsync(
        ManagedFileId managedFileId,
        DocumentAnalysis analysis,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (SqliteCommand resultSql = connection.CreateCommand())
        {
            resultSql.Transaction = transaction;
            resultSql.CommandText =
                """
                INSERT INTO extraction_results (
                    managed_file_id, worker_version, parser_version, candidates_json, completed_at, error)
                VALUES (
                    $managedFileId, $workerVersion, $parserVersion, $analysisJson, $completedAt, NULL)
                ON CONFLICT(managed_file_id) DO UPDATE SET
                    worker_version = excluded.worker_version,
                    parser_version = excluded.parser_version,
                    candidates_json = excluded.candidates_json,
                    completed_at = excluded.completed_at,
                    error = NULL;
                """;
            resultSql.Parameters.AddWithValue("$managedFileId", managedFileId.ToString());
            resultSql.Parameters.AddWithValue("$workerVersion", analysis.WorkerVersion);
            resultSql.Parameters.AddWithValue("$parserVersion", analysis.ParserVersion);
            resultSql.Parameters.AddWithValue(
                "$analysisJson",
                JsonSerializer.Serialize(analysis.Candidates, JsonOptions));
            resultSql.Parameters.AddWithValue("$completedAt", Format(DateTimeOffset.UtcNow));
            await resultSql.ExecuteNonQueryAsync(cancellationToken);
        }

        string? merchantName = analysis.Candidates
            .FirstOrDefault(candidate => candidate.Field == "merchant_name")?.Value;
        string? invoiceNumber = analysis.Candidates
            .FirstOrDefault(candidate => candidate.Field == "invoice_number")?.Value;
        string? totalValue = analysis.Candidates
            .FirstOrDefault(candidate => candidate.Field == "total_minor_units")?.Value;
        long? totalMinorUnits = long.TryParse(
            totalValue,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out long parsedTotal)
                ? parsedTotal
                : null;
        bool needsReview = analysis.NeedsReview
            || string.IsNullOrWhiteSpace(merchantName)
            || string.IsNullOrWhiteSpace(invoiceNumber)
            || totalMinorUnits is null;

        int updatedInvoiceCount;
        await using (SqliteCommand invoiceSql = connection.CreateCommand())
        {
            invoiceSql.Transaction = transaction;
            invoiceSql.CommandText =
                """
                UPDATE invoices
                SET merchant_name = $merchantName,
                    invoice_number = $invoiceNumber,
                    total_minor_units = $totalMinorUnits,
                    needs_review = $needsReview,
                    updated_at = $updatedAt
                WHERE managed_file_id = $managedFileId AND is_user_corrected = 0;
                """;
            invoiceSql.Parameters.AddWithValue(
                "$merchantName",
                string.IsNullOrWhiteSpace(merchantName) ? string.Empty : merchantName.Trim());
            invoiceSql.Parameters.AddWithValue(
                "$invoiceNumber",
                string.IsNullOrWhiteSpace(invoiceNumber) ? string.Empty : invoiceNumber.Trim());
            invoiceSql.Parameters.AddWithValue(
                "$totalMinorUnits",
                totalMinorUnits ?? 0);
            invoiceSql.Parameters.AddWithValue("$needsReview", needsReview ? 1 : 0);
            invoiceSql.Parameters.AddWithValue("$updatedAt", Format(DateTimeOffset.UtcNow));
            invoiceSql.Parameters.AddWithValue("$managedFileId", managedFileId.ToString());
            updatedInvoiceCount = await invoiceSql.ExecuteNonQueryAsync(cancellationToken);
        }

        if (updatedInvoiceCount > 0)
        {
            string[] productNames = analysis.Candidates
                .Where(candidate => candidate.Field == "product_name")
                .Select(candidate => Normalize(candidate.Value))
                .Where(name => name is not null)
                .Cast<string>()
                .ToArray();

            await using SqliteCommand invoiceIdSql = connection.CreateCommand();
            invoiceIdSql.Transaction = transaction;
            invoiceIdSql.CommandText = "SELECT id FROM invoices WHERE managed_file_id = $managedFileId;";
            invoiceIdSql.Parameters.AddWithValue("$managedFileId", managedFileId.ToString());
            string invoiceId = (string?)await invoiceIdSql.ExecuteScalarAsync(cancellationToken)
                ?? throw new KeyNotFoundException($"Invoice for material '{managedFileId}' was not found.");

            await using (SqliteCommand deleteLinesSql = connection.CreateCommand())
            {
                deleteLinesSql.Transaction = transaction;
                deleteLinesSql.CommandText = "DELETE FROM invoice_lines WHERE invoice_id = $invoiceId;";
                deleteLinesSql.Parameters.AddWithValue("$invoiceId", invoiceId);
                await deleteLinesSql.ExecuteNonQueryAsync(cancellationToken);
            }

            for (int sequence = 0; sequence < productNames.Length; sequence++)
            {
                await using SqliteCommand insertLineSql = connection.CreateCommand();
                insertLineSql.Transaction = transaction;
                insertLineSql.CommandText =
                    """
                    INSERT INTO invoice_lines (
                        id, invoice_id, sequence, name, amount_minor_units, is_effective)
                    VALUES ($id, $invoiceId, $sequence, $name, NULL, 1);
                    """;
                insertLineSql.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
                insertLineSql.Parameters.AddWithValue("$invoiceId", invoiceId);
                insertLineSql.Parameters.AddWithValue("$sequence", sequence);
                insertLineSql.Parameters.AddWithValue("$name", productNames[sequence]);
                await insertLineSql.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await using (SqliteCommand stateSql = connection.CreateCommand())
        {
            stateSql.Transaction = transaction;
            stateSql.CommandText =
                """
                UPDATE managed_files
                SET processing_state = 'Processed', processing_error = NULL
                WHERE id = $id;
                """;
            stateSql.Parameters.AddWithValue("$id", managedFileId.ToString());
            await stateSql.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<string?> FindDuplicateFileNameAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sha256,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand sql = connection.CreateCommand();
        sql.Transaction = transaction;
        sql.CommandText =
            """
            SELECT original_file_name
            FROM managed_files
            WHERE sha256 = $sha256
            LIMIT 1;
            """;
        sql.Parameters.AddWithValue("$sha256", sha256);
        return await sql.ExecuteScalarAsync(cancellationToken) as string;
    }

    private static async Task InsertManagedFileAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ManagedFileId fileId,
        OrderId orderId,
        MaterialType materialType,
        string originalFileName,
        string relativePath,
        long byteLength,
        string sha256,
        DateTimeOffset importedAt,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand sql = connection.CreateCommand();
        sql.Transaction = transaction;
        sql.CommandText =
            """
            INSERT INTO managed_files (
                id, order_id, role, original_file_name, relative_path, media_type,
                byte_length, sha256, processing_state, imported_at)
            VALUES (
                $id, $orderId, $role, $originalFileName, $relativePath, $mediaType,
                $byteLength, $sha256, $processingState, $importedAt);
            """;
        sql.Parameters.AddWithValue("$id", fileId.ToString());
        sql.Parameters.AddWithValue("$orderId", orderId.ToString());
        sql.Parameters.AddWithValue("$role", materialType.Role.ToString());
        sql.Parameters.AddWithValue("$mediaType", materialType.MediaType);
        sql.Parameters.AddWithValue("$originalFileName", originalFileName);
        sql.Parameters.AddWithValue("$relativePath", relativePath);
        sql.Parameters.AddWithValue("$byteLength", byteLength);
        sql.Parameters.AddWithValue("$sha256", sha256);
        sql.Parameters.AddWithValue("$processingState", materialType.ProcessingState.ToString());
        sql.Parameters.AddWithValue("$importedAt", Format(importedAt));
        await sql.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertInvoicePlaceholderAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ManagedFileId fileId,
        OrderId orderId,
        DateTimeOffset importedAt,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand sql = connection.CreateCommand();
        sql.Transaction = transaction;
        sql.CommandText =
            """
            INSERT INTO invoices (id, order_id, managed_file_id, updated_at)
            VALUES ($id, $orderId, $managedFileId, $updatedAt);
            """;
        sql.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        sql.Parameters.AddWithValue("$orderId", orderId.ToString());
        sql.Parameters.AddWithValue("$managedFileId", fileId.ToString());
        sql.Parameters.AddWithValue("$updatedAt", Format(importedAt));
        await sql.ExecuteNonQueryAsync(cancellationToken);
    }

    private string ResolveManagedPath(string relativePath)
    {
        string path = Path.GetFullPath(Path.Combine(_libraryRoot, relativePath));
        string rootWithSeparator = _libraryRoot.EndsWith(Path.DirectorySeparatorChar)
            ? _libraryRoot
            : _libraryRoot + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Managed file path escapes the library root.");
        }

        return path;
    }

    private void CleanupStagedDeletions()
    {
        string deletingRoot = ResolveManagedPath(Path.Combine("staging", "deleting"));
        if (!Directory.Exists(deletingRoot))
        {
            return;
        }

        foreach (string directory in Directory.EnumerateDirectories(deletingRoot))
        {
            string verifiedPath = Path.GetFullPath(directory);
            string rootWithSeparator = deletingRoot.EndsWith(Path.DirectorySeparatorChar)
                ? deletingRoot
                : deletingRoot + Path.DirectorySeparatorChar;
            if (verifiedPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteDirectory(verifiedPath);
            }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // A later application start retries staged deletion after file handles are released.
        }
        catch (UnauthorizedAccessException)
        {
            // A later application start retries staged deletion after permissions change.
        }
    }

    private static MaterialImportItem Rejected(string sourcePath, string message) =>
        new(sourcePath, MaterialImportOutcome.Rejected, null, message);

    private static MaterialType? ClassifyMaterial(string path, ManagedFileRole role) =>
        (role, Path.GetExtension(path).ToLowerInvariant()) switch
        {
            (ManagedFileRole.InvoicePdf, ".pdf") =>
                new MaterialType(ManagedFileRole.InvoicePdf, "invoices", ".pdf", "application/pdf", MaterialProcessingState.Pending),
            (ManagedFileRole.OrderScreenshot, ".pdf") =>
                new MaterialType(ManagedFileRole.OrderScreenshot, "supporting-materials", ".pdf", "application/pdf", MaterialProcessingState.Stored),
            (ManagedFileRole.OrderScreenshot, ".png") =>
                new MaterialType(ManagedFileRole.OrderScreenshot, "supporting-materials", ".png", "image/png", MaterialProcessingState.Stored),
            (ManagedFileRole.OrderScreenshot, ".jpg") =>
                new MaterialType(ManagedFileRole.OrderScreenshot, "supporting-materials", ".jpg", "image/jpeg", MaterialProcessingState.Stored),
            (ManagedFileRole.OrderScreenshot, ".jpeg") =>
                new MaterialType(ManagedFileRole.OrderScreenshot, "supporting-materials", ".jpeg", "image/jpeg", MaterialProcessingState.Stored),
            _ => null,
        };

    private static async Task<bool> HasExpectedSignatureAsync(
        string path,
        MaterialType materialType,
        CancellationToken cancellationToken)
    {
        byte[] header = new byte[1024];
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: header.Length,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        int bytesRead = await stream.ReadAsync(header, cancellationToken);
        ReadOnlySpan<byte> content = header.AsSpan(0, bytesRead);

        return materialType.Extension switch
        {
            ".pdf" => content.IndexOf("%PDF-"u8) >= 0,
            ".png" => bytesRead >= 8
                && content[0] == 0x89
                && content[1] == 0x50
                && content[2] == 0x4E
                && content[3] == 0x47
                && content[4] == 0x0D
                && content[5] == 0x0A
                && content[6] == 0x1A
                && content[7] == 0x0A,
            ".jpg" or ".jpeg" => bytesRead >= 3
                && content[0] == 0xFF
                && content[1] == 0xD8
                && content[2] == 0xFF,
            _ => false,
        };
    }

    private sealed record MaterialType(
        ManagedFileRole Role,
        string FolderName,
        string Extension,
        string MediaType,
        MaterialProcessingState ProcessingState);

    private sealed record InvoiceRow(
        InvoiceId Id,
        ManagedFileId ManagedFileId,
        string OriginalFileName,
        string MerchantName,
        string InvoiceNumber,
        long TotalMinorUnits,
        bool NeedsReview);

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        SqliteConnection connection = new(ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using SqliteCommand pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000; PRAGMA journal_mode = WAL;";
        await pragma.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand sql = connection.CreateCommand();
        sql.CommandText = commandText;
        await sql.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseNullableTimestamp(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static IReadOnlyList<string> SplitAggregate(string value) =>
        string.IsNullOrEmpty(value)
            ? []
            : value.Split(AggregateSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
