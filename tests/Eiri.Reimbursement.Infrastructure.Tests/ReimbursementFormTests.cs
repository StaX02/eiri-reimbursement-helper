using Eiri.Reimbursement.Core.Documents;
using Eiri.Reimbursement.Core.Invoices;
using Eiri.Reimbursement.Core.Materials;
using Eiri.Reimbursement.Core.Orders;
using Eiri.Reimbursement.Core.Reimbursements;
using Eiri.Reimbursement.Infrastructure.DataTransfer;
using Eiri.Reimbursement.Infrastructure.Sqlite;

namespace Eiri.Reimbursement.Infrastructure.Tests;

public sealed class ReimbursementFormTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "eiri-forms-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private async Task<SqliteReimbursementWorkspace> CreateWorkspace(IDocumentProcessor? processor = null)
    {
        SqliteReimbursementWorkspace workspace = new(Path.Combine(_root, "library"), processor);
        await workspace.InitializeAsync();
        return workspace;
    }
    private async Task<string> FileAsync(string name, string text)
    {
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, name);
        await File.WriteAllTextAsync(path, text);
        return path;
    }

    [Fact]
    public async Task CreatesBindingsAtomicallyAndInitializesAmountFromInvoices()
    {
        var workspace = await CreateWorkspace();
        var a = await workspace.CreateOrderAsync(new(OrderPlatform.JD));
        var b = await workspace.CreateOrderAsync(new(OrderPlatform.Taobao));
        await workspace.ImportMaterialsAsync(new(a, [await FileAsync("invoice.pdf", "%PDF-test")], ManagedFileRole.InvoicePdf));
        InvoiceDetail invoice = Assert.Single((await workspace.GetOrderAsync(a))!.Invoices);
        await workspace.UpdateInvoiceAsync(new(invoice.Id, "商家", "123", 12345, []));
        Guid id = await workspace.CreateReimbursementAsync([a, b]);
        ReimbursementForm form = (await workspace.GetReimbursementAsync(id))!.Form;
        Assert.Equal(12345, form.TotalMinorUnits);
        Assert.Null(form.ApplicationDate);
        Assert.Equal("", form.Content);
        Assert.Equal(2, form.OrderIds.Count);
        var c = await workspace.CreateOrderAsync(new(OrderPlatform.Other));
        await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.CreateReimbursementAsync([c, a]));
        Assert.Single(await workspace.ListReimbursementsAsync());
        Assert.Equal("暂未绑定", (await workspace.GetOrderAsync(c))!.ReimbursementDisplay);
        await workspace.UpdateReimbursementAsync(new(id, new(2026, 8, 25), "材料费", "PCB", 507874));
        Assert.Equal("PCB", (await workspace.GetOrderAsync(a))!.ReimbursementDisplay);
        Assert.Equal("PCB", (await workspace.SearchOrdersAsync(new())).Single(o => o.Id == b).ReimbursementDisplay);
        await workspace.DeleteOrderAsync(a);
        await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.DeleteOrderAsync(b));
        Assert.Single((await workspace.GetReimbursementAsync(id))!.Form.OrderIds);
    }

    [Fact]
    public async Task FirstPdfFillsFieldsAndLaterPdfPreservesValuesAndZeroAmount()
    {
        Processor processor = new();
        var workspace = await CreateWorkspace(processor);
        Guid id = await workspace.CreateReimbursementAsync([await workspace.CreateOrderAsync(new(OrderPlatform.JD))]);
        await workspace.ImportReimbursementFilesAsync(id, [await FileAsync("first.pdf", "%PDF-first")]);
        var form = (await workspace.GetReimbursementAsync(id))!.Form;
        Assert.Equal(new DateOnly(2026, 8, 25), form.ApplicationDate);
        Assert.Equal(507874, form.TotalMinorUnits);
        Assert.Equal("材料费", form.ReimbursementType);
        Assert.Equal("IGCT驱动芯片测试PCB", form.Content);
        await workspace.UpdateReimbursementAsync(new(id, form.ApplicationDate, "", "人工校正", 0));
        await workspace.ImportReimbursementFilesAsync(id, [await FileAsync("second.pdf", "%PDF-second")]);
        form = (await workspace.GetReimbursementAsync(id))!.Form;
        Assert.Equal("人工校正", form.Content);
        Assert.Equal("材料费", form.ReimbursementType);
        Assert.Equal(0, form.TotalMinorUnits);
        Assert.Equal(DocumentKind.ReimbursementPdf, processor.LastKind);
    }

    [Fact]
    public async Task FailedRecognitionKeepsAttachmentAndBackupRestoresItWithBindings()
    {
        var workspace = await CreateWorkspace();
        OrderId order = await workspace.CreateOrderAsync(new(OrderPlatform.JD));
        Guid id = await workspace.CreateReimbursementAsync([order]);
        string file = await FileAsync("broken.pdf", "%PDF-broken");
        var result = await workspace.ImportReimbursementFilesAsync(id, [file]);
        Assert.Equal(1, result.ImportedCount);
        Assert.Single(result.Messages);
        Assert.Equal(0, (await workspace.ImportReimbursementFilesAsync(id, [file])).ImportedCount);
        string backup = Path.Combine(_root, "backup.eirbackup");
        await new WholeLibraryBackupService(Path.Combine(_root, "library")).CreateBackupAsync(backup);
        string restored = Path.Combine(_root, "restored");
        await new WholeLibraryBackupService(restored).RestoreBackupAsync(backup);
        var restoredWorkspace = new SqliteReimbursementWorkspace(restored);
        await restoredWorkspace.InitializeAsync();
        var detail = (await restoredWorkspace.GetReimbursementAsync(id))!;
        var attachment = Assert.Single(detail.Attachments);
        Assert.NotNull(attachment.ProcessingError);
        Assert.Equal("%PDF-broken", await File.ReadAllTextAsync(attachment.ManagedPath));
        Assert.Equal(id, (await restoredWorkspace.GetOrderAsync(order))!.ReimbursementId);
    }

    private sealed class Processor : IDocumentProcessor
    {
        public DocumentKind LastKind { get; private set; }
        public Task<DocumentAnalysis> AnalyzeAsync(DocumentJob job, CancellationToken cancellationToken = default)
        {
            LastKind = job.Kind;
            return Task.FromResult(new DocumentAnalysis("test", "test", [],
            [
                new("application_date", "2026-08-25", 1, "test", 1),
                new("reimbursement_type", "材料费", 1, "test", 1),
                new("reimbursement_content", "IGCT驱动芯片测试PCB", 1, "test", 1),
                new("total_minor_units", "507874", 1, "test", 1),
                new("total_minor_units", "999999", 1, "test", 2),
            ], false));
        }
    }
}
