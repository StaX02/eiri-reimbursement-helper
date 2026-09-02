using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Eiri.Reimbursement.Core.DataTransfer;
using Eiri.Reimbursement.Core.Materials;
using Eiri.Reimbursement.Core.Orders;
using Eiri.Reimbursement.Infrastructure.DataTransfer;
using Eiri.Reimbursement.Infrastructure.Sqlite;

namespace Eiri.Reimbursement.Infrastructure.Tests;

public sealed class WholeLibraryBackupServiceTests : IDisposable
{
    private readonly string _testRoot = Path.Combine(
        Path.GetTempPath(),
        "eiri-backup-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task BackupPackageRestoresOrdersAndManagedFilesAsAWholeLibrary()
    {
        string sourceRoot = Path.Combine(_testRoot, "source");
        SqliteReimbursementWorkspace sourceWorkspace = new(sourceRoot);
        await sourceWorkspace.InitializeAsync();
        OrderId sourceOrderId = await sourceWorkspace.CreateOrderAsync(
            new CreateOrderCommand(OrderPlatform.JD));
        string sourceInvoicePath = Path.Combine(_testRoot, "invoice.pdf");
        byte[] invoiceContent = "%PDF-1.7 backup invoice"u8.ToArray();
        Directory.CreateDirectory(_testRoot);
        await File.WriteAllBytesAsync(sourceInvoicePath, invoiceContent);
        await sourceWorkspace.ImportMaterialsAsync(
            new ImportMaterialsCommand(
                sourceOrderId,
                [sourceInvoicePath],
                ManagedFileRole.InvoicePdf));

        string packagePath = Path.Combine(_testRoot, "library.eirbackup");
        IWholeLibraryBackupService sourceBackup = new WholeLibraryBackupService(sourceRoot);
        await sourceBackup.CreateBackupAsync(packagePath);

        string restoredRoot = Path.Combine(_testRoot, "restored");
        SqliteReimbursementWorkspace restoredWorkspace = new(restoredRoot);
        await restoredWorkspace.InitializeAsync();
        await restoredWorkspace.CreateOrderAsync(new CreateOrderCommand(OrderPlatform.Taobao));
        IWholeLibraryBackupService restoredBackup = new WholeLibraryBackupService(restoredRoot);

        await restoredBackup.RestoreBackupAsync(packagePath);

        IReadOnlyList<OrderListItem> restoredOrders = await restoredWorkspace.SearchOrdersAsync(
            new OrderQuery(null, null, 0, 100));
        OrderListItem restoredOrder = Assert.Single(restoredOrders);
        Assert.Equal(sourceOrderId, restoredOrder.Id);
        OrderDetail restoredDetail = Assert.IsType<OrderDetail>(
            await restoredWorkspace.GetOrderAsync(sourceOrderId));
        ManagedMaterial restoredInvoice = Assert.Single(restoredDetail.Materials);
        Assert.Equal(invoiceContent, await File.ReadAllBytesAsync(restoredInvoice.ManagedPath));
    }

    [Fact]
    public async Task ImportRejectsOrdinaryArchiveAndPreservesExistingLibrary()
    {
        string libraryRoot = Path.Combine(_testRoot, "library");
        SqliteReimbursementWorkspace workspace = new(libraryRoot);
        await workspace.InitializeAsync();
        OrderId existingOrderId = await workspace.CreateOrderAsync(
            new CreateOrderCommand(OrderPlatform.Taobao));
        string ordinaryArchivePath = Path.Combine(_testRoot, "ordinary.zip");
        Directory.CreateDirectory(_testRoot);
        using (ZipArchive archive = ZipFile.Open(ordinaryArchivePath, ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry = archive.CreateEntry("notes.txt");
            await using StreamWriter writer = new(entry.Open());
            await writer.WriteAsync("这不是数据库备份包");
        }

        IWholeLibraryBackupService backup = new WholeLibraryBackupService(libraryRoot);

        await Assert.ThrowsAsync<InvalidDataException>(() => backup.RestoreBackupAsync(ordinaryArchivePath));

        OrderListItem remainingOrder = Assert.Single(await workspace.SearchOrdersAsync(new OrderQuery()));
        Assert.Equal(existingOrderId, remainingOrder.Id);
    }

    [Fact]
    public async Task ImportRejectsTamperedPackageAndPreservesExistingLibrary()
    {
        string sourceRoot = Path.Combine(_testRoot, "source");
        SqliteReimbursementWorkspace sourceWorkspace = new(sourceRoot);
        await sourceWorkspace.InitializeAsync();
        await sourceWorkspace.CreateOrderAsync(new CreateOrderCommand(OrderPlatform.JD));
        string packagePath = Path.Combine(_testRoot, "tampered.eirbackup");
        IWholeLibraryBackupService sourceBackup = new WholeLibraryBackupService(sourceRoot);
        await sourceBackup.CreateBackupAsync(packagePath);

        using (ZipArchive archive = ZipFile.Open(packagePath, ZipArchiveMode.Update))
        {
            ZipArchiveEntry databaseEntry = Assert.IsType<ZipArchiveEntry>(archive.GetEntry("library.db"));
            databaseEntry.Delete();
            ZipArchiveEntry replacement = archive.CreateEntry("library.db");
            await using Stream replacementStream = replacement.Open();
            await replacementStream.WriteAsync("tampered"u8.ToArray());
        }

        string targetRoot = Path.Combine(_testRoot, "target");
        SqliteReimbursementWorkspace targetWorkspace = new(targetRoot);
        await targetWorkspace.InitializeAsync();
        OrderId existingOrderId = await targetWorkspace.CreateOrderAsync(
            new CreateOrderCommand(OrderPlatform.Other));
        IWholeLibraryBackupService targetBackup = new WholeLibraryBackupService(targetRoot);

        await Assert.ThrowsAsync<InvalidDataException>(() => targetBackup.RestoreBackupAsync(packagePath));

        OrderListItem remainingOrder = Assert.Single(
            await targetWorkspace.SearchOrdersAsync(new OrderQuery()));
        Assert.Equal(existingOrderId, remainingOrder.Id);
    }

    [Fact]
    public async Task ExportRejectsLibraryWhenDatabaseReferencesMissingManagedFile()
    {
        string libraryRoot = Path.Combine(_testRoot, "library");
        SqliteReimbursementWorkspace workspace = new(libraryRoot);
        await workspace.InitializeAsync();
        OrderId orderId = await workspace.CreateOrderAsync(
            new CreateOrderCommand(OrderPlatform.JD));
        string invoicePath = Path.Combine(_testRoot, "invoice.pdf");
        Directory.CreateDirectory(_testRoot);
        await File.WriteAllBytesAsync(invoicePath, "%PDF-1.7 missing later"u8.ToArray());
        await workspace.ImportMaterialsAsync(new ImportMaterialsCommand(
            orderId,
            [invoicePath],
            ManagedFileRole.InvoicePdf));
        ManagedMaterial material = Assert.Single(Assert.IsType<OrderDetail>(
            await workspace.GetOrderAsync(orderId)).Materials);
        File.Delete(material.ManagedPath);
        string packagePath = Path.Combine(_testRoot, "incomplete.eirbackup");
        IWholeLibraryBackupService backup = new WholeLibraryBackupService(libraryRoot);

        await Assert.ThrowsAsync<InvalidDataException>(() => backup.CreateBackupAsync(packagePath));

        Assert.False(File.Exists(packagePath));
    }

    [Fact]
    public async Task ImportRejectsArchivePathTraversalWithoutWritingOutsideLibrary()
    {
        string libraryRoot = Path.Combine(_testRoot, "library");
        SqliteReimbursementWorkspace workspace = new(libraryRoot);
        await workspace.InitializeAsync();
        OrderId existingOrderId = await workspace.CreateOrderAsync(
            new CreateOrderCommand(OrderPlatform.Taobao));
        string packagePath = Path.Combine(_testRoot, "traversal.eirbackup");
        byte[] escapedContent = "escape"u8.ToArray();
        Directory.CreateDirectory(_testRoot);
        using (ZipArchive archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
        {
            ZipArchiveEntry escapedEntry = archive.CreateEntry("../escape.txt");
            await using (Stream escapedStream = escapedEntry.Open())
            {
                await escapedStream.WriteAsync(escapedContent);
            }

            ZipArchiveEntry manifestEntry = archive.CreateEntry("manifest.json");
            await using Stream manifestStream = manifestEntry.Open();
            await JsonSerializer.SerializeAsync(manifestStream, new
            {
                formatId = "eiri-reimbursement-backup",
                formatVersion = 1,
                createdAt = DateTimeOffset.UtcNow,
                files = new[]
                {
                    new
                    {
                        path = "../escape.txt",
                        length = escapedContent.Length,
                        sha256 = Convert.ToHexStringLower(SHA256.HashData(escapedContent)),
                    },
                },
            });
        }

        IWholeLibraryBackupService backup = new WholeLibraryBackupService(libraryRoot);

        await Assert.ThrowsAsync<InvalidDataException>(() => backup.RestoreBackupAsync(packagePath));

        Assert.False(File.Exists(Path.Combine(_testRoot, "escape.txt")));
        OrderListItem remainingOrder = Assert.Single(await workspace.SearchOrdersAsync(new OrderQuery()));
        Assert.Equal(existingOrderId, remainingOrder.Id);
    }

    [Fact]
    public async Task BackupPackageExcludesFilesNotManagedByDatabase()
    {
        string libraryRoot = Path.Combine(_testRoot, "library");
        SqliteReimbursementWorkspace workspace = new(libraryRoot);
        await workspace.InitializeAsync();
        string orphanPath = Path.Combine(libraryRoot, "originals", "orphan.pdf");
        await File.WriteAllBytesAsync(orphanPath, "%PDF-1.7 orphan"u8.ToArray());
        string packagePath = Path.Combine(_testRoot, "without-orphan.eirbackup");
        IWholeLibraryBackupService backup = new WholeLibraryBackupService(libraryRoot);

        await backup.CreateBackupAsync(packagePath);

        using ZipArchive archive = ZipFile.OpenRead(packagePath);
        Assert.DoesNotContain(
            archive.Entries,
            entry => string.Equals(
                entry.FullName,
                "originals/orphan.pdf",
                StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            archive.Entries,
            entry => entry.FullName.EndsWith("-wal", StringComparison.OrdinalIgnoreCase)
                || entry.FullName.EndsWith("-shm", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }
}
