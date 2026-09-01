using Eiri.Reimbursement.Core;
using Eiri.Reimbursement.Core.Documents;
using Eiri.Reimbursement.Core.Invoices;
using Eiri.Reimbursement.Core.Materials;
using Eiri.Reimbursement.Core.Orders;
using Eiri.Reimbursement.Infrastructure.Sqlite;

namespace Eiri.Reimbursement.Infrastructure.Tests;

public sealed class SqliteReimbursementWorkspaceTests : IAsyncLifetime
{
    private readonly string _libraryRoot = Path.Combine(
        Path.GetTempPath(),
        "eiri-reimbursement-tests",
        Guid.NewGuid().ToString("N"));

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(_libraryRoot))
        {
            Directory.Delete(_libraryRoot, recursive: true);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task CreatesSearchesAndUpdatesOrderMilestones()
    {
        SqliteReimbursementWorkspace workspace = new(_libraryRoot);
        await workspace.InitializeAsync();

        OrderId orderId = await workspace.CreateOrderAsync(
            new CreateOrderCommand(OrderPlatform.JD, "JD-2026-001"));

        DateTimeOffset exportedAt = DateTimeOffset.UtcNow;
        await workspace.SetMilestoneAsync(
            new SetMilestoneCommand(orderId, Milestone.Exported, exportedAt));

        IReadOnlyList<OrderListItem> orders = await workspace.SearchOrdersAsync(
            new OrderQuery(SearchText: "2026-001"));

        OrderListItem order = Assert.Single(orders);
        Assert.Equal(orderId, order.Id);
        Assert.Equal(OrderPlatform.JD, order.Platform);
        Assert.Equal("JD-2026-001", order.ExternalOrderNumber);
        Assert.Equal(exportedAt.ToUnixTimeSeconds(), order.ExportedAt?.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task ImportedInvoiceIsAvailableFromOrderDetailAfterSourceIsRemoved()
    {
        IReimbursementWorkspace workspace = new SqliteReimbursementWorkspace(_libraryRoot);
        await workspace.InitializeAsync();
        OrderId orderId = await workspace.CreateOrderAsync(new CreateOrderCommand(OrderPlatform.Taobao));
        string sourcePath = Path.Combine(_libraryRoot, "source-invoice.pdf");
        byte[] expectedContent = "%PDF-1.7 eiri invoice"u8.ToArray();
        await File.WriteAllBytesAsync(sourcePath, expectedContent);

        ImportMaterialsResult result = await workspace.ImportMaterialsAsync(
            new ImportMaterialsCommand(orderId, [sourcePath]));
        File.Delete(sourcePath);

        OrderDetail? detail = await workspace.GetOrderAsync(orderId);
        ManagedMaterial material = Assert.Single(Assert.IsType<OrderDetail>(detail).Materials);
        Assert.Equal(MaterialImportOutcome.Imported, Assert.Single(result.Items).Outcome);
        Assert.Equal(ManagedFileRole.InvoicePdf, material.Role);
        Assert.Equal("source-invoice.pdf", material.OriginalFileName);
        Assert.Equal(expectedContent, await File.ReadAllBytesAsync(material.ManagedPath));
    }

    [Fact]
    public async Task SameContentIsReportedAsDuplicateAndStoredOnce()
    {
        IReimbursementWorkspace workspace = new SqliteReimbursementWorkspace(_libraryRoot);
        await workspace.InitializeAsync();
        OrderId orderId = await workspace.CreateOrderAsync(new CreateOrderCommand(OrderPlatform.JD));
        string firstPath = Path.Combine(_libraryRoot, "invoice-a.pdf");
        string secondPath = Path.Combine(_libraryRoot, "invoice-b.pdf");
        byte[] content = "%PDF-1.7 duplicate invoice"u8.ToArray();
        await File.WriteAllBytesAsync(firstPath, content);
        await File.WriteAllBytesAsync(secondPath, content);

        ImportMaterialsResult result = await workspace.ImportMaterialsAsync(
            new ImportMaterialsCommand(orderId, [firstPath, secondPath]));

        Assert.Collection(
            result.Items,
            item => Assert.Equal(MaterialImportOutcome.Imported, item.Outcome),
            item => Assert.Equal(MaterialImportOutcome.Duplicate, item.Outcome));
        Assert.Single(Assert.IsType<OrderDetail>(await workspace.GetOrderAsync(orderId)).Materials);
    }

    [Fact]
    public async Task ImportedPngIsClassifiedAsOrderScreenshot()
    {
        IReimbursementWorkspace workspace = new SqliteReimbursementWorkspace(_libraryRoot);
        await workspace.InitializeAsync();
        OrderId orderId = await workspace.CreateOrderAsync(new CreateOrderCommand(OrderPlatform.Taobao));
        string screenshotPath = Path.Combine(_libraryRoot, "taobao-order.png");
        await File.WriteAllBytesAsync(screenshotPath, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        ImportMaterialsResult result = await workspace.ImportMaterialsAsync(
            new ImportMaterialsCommand(orderId, [screenshotPath]));

        ManagedMaterial material = Assert.Single(Assert.IsType<OrderDetail>(
            await workspace.GetOrderAsync(orderId)).Materials);
        Assert.Equal(MaterialImportOutcome.Imported, Assert.Single(result.Items).Outcome);
        Assert.Equal(ManagedFileRole.OrderScreenshot, material.Role);
        Assert.Equal("image/png", material.MediaType);
    }

    [Fact]
    public async Task FileWithPdfExtensionAndInvalidContentIsRejectedWithoutAddingMaterial()
    {
        IReimbursementWorkspace workspace = new SqliteReimbursementWorkspace(_libraryRoot);
        await workspace.InitializeAsync();
        OrderId orderId = await workspace.CreateOrderAsync(new CreateOrderCommand(OrderPlatform.Other));
        string invalidPdfPath = Path.Combine(_libraryRoot, "renamed-text.pdf");
        await File.WriteAllTextAsync(invalidPdfPath, "this is not a pdf");

        ImportMaterialsResult result = await workspace.ImportMaterialsAsync(
            new ImportMaterialsCommand(orderId, [invalidPdfPath]));

        MaterialImportItem item = Assert.Single(result.Items);
        Assert.Equal(MaterialImportOutcome.Rejected, item.Outcome);
        Assert.Empty(Assert.IsType<OrderDetail>(await workspace.GetOrderAsync(orderId)).Materials);
    }

    [Fact]
    public async Task CorrectedInvoiceFieldsAreAvailableFromOrderDetail()
    {
        IReimbursementWorkspace workspace = new SqliteReimbursementWorkspace(_libraryRoot);
        await workspace.InitializeAsync();
        OrderId orderId = await workspace.CreateOrderAsync(new CreateOrderCommand(OrderPlatform.JD));
        string invoicePath = Path.Combine(_libraryRoot, "invoice-to-correct.pdf");
        await File.WriteAllBytesAsync(invoicePath, "%PDF-1.7 editable invoice"u8.ToArray());
        await workspace.ImportMaterialsAsync(new ImportMaterialsCommand(orderId, [invoicePath]));
        InvoiceDetail invoice = Assert.Single(Assert.IsType<OrderDetail>(
            await workspace.GetOrderAsync(orderId)).Invoices);

        await workspace.UpdateInvoiceAsync(new UpdateInvoiceCommand(
            invoice.Id,
            "京东自营",
            "25312000000000123456",
            15_990,
            [new InvoiceLineCorrection("机械键盘"), new InvoiceLineCorrection("键帽")]));

        InvoiceDetail corrected = Assert.Single(Assert.IsType<OrderDetail>(
            await workspace.GetOrderAsync(orderId)).Invoices);
        Assert.Equal("京东自营", corrected.MerchantName);
        Assert.Equal("25312000000000123456", corrected.InvoiceNumber);
        Assert.Equal(159.90m, corrected.TotalAmount);
        Assert.Equal("机械键盘 等 2 项", corrected.PrimaryProductDisplay);
        Assert.False(corrected.NeedsReview);
    }

    [Fact]
    public async Task MultipleInvoiceLinesAreSummarizedInOrderList()
    {
        IReimbursementWorkspace workspace = new SqliteReimbursementWorkspace(_libraryRoot);
        await workspace.InitializeAsync();
        OrderId orderId = await workspace.CreateOrderAsync(new CreateOrderCommand(OrderPlatform.JD));
        string invoicePath = Path.Combine(_libraryRoot, "invoice-with-lines.pdf");
        await File.WriteAllBytesAsync(invoicePath, "%PDF-1.7 line summary"u8.ToArray());
        await workspace.ImportMaterialsAsync(new ImportMaterialsCommand(orderId, [invoicePath]));
        InvoiceDetail invoice = Assert.Single(Assert.IsType<OrderDetail>(
            await workspace.GetOrderAsync(orderId)).Invoices);
        await workspace.UpdateInvoiceAsync(new UpdateInvoiceCommand(
            invoice.Id,
            "京东自营",
            "25312000000000999999",
            20_000,
            [new InvoiceLineCorrection("显示器"), new InvoiceLineCorrection("支架")]));

        OrderListItem order = Assert.Single(await workspace.SearchOrdersAsync(new OrderQuery()));

        Assert.Equal("显示器 等 2 项", order.ProductDisplay);
    }

    [Fact]
    public async Task DeletingOrderRemovesItAndItsManagedFiles()
    {
        IReimbursementWorkspace workspace = new SqliteReimbursementWorkspace(_libraryRoot);
        await workspace.InitializeAsync();
        OrderId orderId = await workspace.CreateOrderAsync(new CreateOrderCommand(OrderPlatform.Taobao));
        string invoicePath = Path.Combine(_libraryRoot, "invoice-to-delete.pdf");
        string screenshotPath = Path.Combine(_libraryRoot, "screenshot-to-delete.png");
        await File.WriteAllBytesAsync(invoicePath, "%PDF-1.7 delete me"u8.ToArray());
        await File.WriteAllBytesAsync(screenshotPath, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        await workspace.ImportMaterialsAsync(
            new ImportMaterialsCommand(orderId, [invoicePath, screenshotPath]));
        string[] managedPaths = Assert.IsType<OrderDetail>(
            await workspace.GetOrderAsync(orderId)).Materials.Select(material => material.ManagedPath).ToArray();

        await workspace.DeleteOrderAsync(orderId);

        Assert.Null(await workspace.GetOrderAsync(orderId));
        Assert.DoesNotContain(await workspace.SearchOrdersAsync(new OrderQuery()), order => order.Id == orderId);
        Assert.All(managedPaths, path => Assert.False(File.Exists(path)));
        Assert.True(File.Exists(invoicePath));
        Assert.True(File.Exists(screenshotPath));
    }

    [Fact]
    public async Task AnalyzedInvoiceReturnsExtractedTextAndUpdatesMaterialState()
    {
        DocumentAnalysis expected = new(
            "test-worker",
            "test-parser",
            [new TextBlock("发票号码：25312000000000123456", 1, new TextBounds(0, 0, 100, 20), 1, "pdf-text")],
            [],
            true);
        IReimbursementWorkspace workspace = new SqliteReimbursementWorkspace(
            _libraryRoot,
            new FixedDocumentProcessor(expected));
        await workspace.InitializeAsync();
        OrderId orderId = await workspace.CreateOrderAsync(new CreateOrderCommand(OrderPlatform.JD));
        string invoicePath = Path.Combine(_libraryRoot, "invoice-to-analyze.pdf");
        await File.WriteAllBytesAsync(invoicePath, "%PDF-1.7 analyze me"u8.ToArray());
        await workspace.ImportMaterialsAsync(new ImportMaterialsCommand(orderId, [invoicePath]));
        InvoiceDetail invoice = Assert.Single(Assert.IsType<OrderDetail>(
            await workspace.GetOrderAsync(orderId)).Invoices);

        DocumentAnalysis actual = await workspace.AnalyzeInvoiceAsync(invoice.Id);

        Assert.Equal("发票号码：25312000000000123456", Assert.Single(actual.TextBlocks).Text);
        ManagedMaterial material = Assert.Single(Assert.IsType<OrderDetail>(
            await workspace.GetOrderAsync(orderId)).Materials);
        Assert.Equal("Processed", material.ProcessingState);
    }

    [Fact]
    public async Task AnalyzedInvoicePopulatesExtractedInvoiceFields()
    {
        DocumentAnalysis expected = new(
            "test-worker",
            "test-parser",
            [],
            [
                new FieldCandidate("merchant_name", "深圳德诺嘉电子有限公司", 1, "invoice-profile"),
                new FieldCandidate("invoice_number", "25952000000269819544", 1, "invoice-profile"),
                new FieldCandidate("total_minor_units", "778800", 1, "invoice-profile"),
            ],
            false);
        IReimbursementWorkspace workspace = new SqliteReimbursementWorkspace(
            _libraryRoot,
            new FixedDocumentProcessor(expected));
        await workspace.InitializeAsync();
        OrderId orderId = await workspace.CreateOrderAsync(new CreateOrderCommand(OrderPlatform.Other));
        string invoicePath = Path.Combine(_libraryRoot, "invoice-fields.pdf");
        await File.WriteAllBytesAsync(invoicePath, "%PDF-1.7 extracted fields"u8.ToArray());
        await workspace.ImportMaterialsAsync(new ImportMaterialsCommand(orderId, [invoicePath]));
        InvoiceDetail invoice = Assert.Single(Assert.IsType<OrderDetail>(
            await workspace.GetOrderAsync(orderId)).Invoices);

        await workspace.AnalyzeInvoiceAsync(invoice.Id);

        InvoiceDetail analyzedInvoice = Assert.Single(Assert.IsType<OrderDetail>(
            await workspace.GetOrderAsync(orderId)).Invoices);
        Assert.Equal("深圳德诺嘉电子有限公司", analyzedInvoice.MerchantName);
        Assert.Equal("25952000000269819544", analyzedInvoice.InvoiceNumber);
        Assert.Equal(778_800, analyzedInvoice.TotalMinorUnits);
        Assert.False(analyzedInvoice.NeedsReview);
    }

    [Fact]
    public async Task ReanalysisUpdatesMachineFieldsButPreservesUserCorrections()
    {
        DocumentAnalysis firstAnalysis = AnalysisWithFields("商家甲", "10000000000000000001", "100");
        DocumentAnalysis secondAnalysis = AnalysisWithFields("商家乙", "10000000000000000002", "200");
        DocumentAnalysis thirdAnalysis = AnalysisWithFields("商家丙", "10000000000000000003", "300");
        IReimbursementWorkspace workspace = new SqliteReimbursementWorkspace(
            _libraryRoot,
            new SequenceDocumentProcessor(firstAnalysis, secondAnalysis, thirdAnalysis));
        await workspace.InitializeAsync();
        OrderId orderId = await workspace.CreateOrderAsync(new CreateOrderCommand(OrderPlatform.Other));
        string invoicePath = Path.Combine(_libraryRoot, "invoice-reanalysis.pdf");
        await File.WriteAllBytesAsync(invoicePath, "%PDF-1.7 reanalysis"u8.ToArray());
        await workspace.ImportMaterialsAsync(new ImportMaterialsCommand(orderId, [invoicePath]));
        InvoiceDetail invoice = Assert.Single(Assert.IsType<OrderDetail>(
            await workspace.GetOrderAsync(orderId)).Invoices);

        await workspace.AnalyzeInvoiceAsync(invoice.Id);
        await workspace.AnalyzeInvoiceAsync(invoice.Id);

        InvoiceDetail reanalyzed = Assert.Single(Assert.IsType<OrderDetail>(
            await workspace.GetOrderAsync(orderId)).Invoices);
        Assert.Equal("商家乙", reanalyzed.MerchantName);
        Assert.Equal(200, reanalyzed.TotalMinorUnits);

        await workspace.UpdateInvoiceAsync(new UpdateInvoiceCommand(
            invoice.Id,
            "人工校正商家",
            "20000000000000000001",
            999,
            []));
        await workspace.AnalyzeInvoiceAsync(invoice.Id);

        InvoiceDetail corrected = Assert.Single(Assert.IsType<OrderDetail>(
            await workspace.GetOrderAsync(orderId)).Invoices);
        Assert.Equal("人工校正商家", corrected.MerchantName);
        Assert.Equal("20000000000000000001", corrected.InvoiceNumber);
        Assert.Equal(999, corrected.TotalMinorUnits);
    }

    private static DocumentAnalysis AnalysisWithFields(
        string merchantName,
        string invoiceNumber,
        string totalMinorUnits) => new(
            "test-worker",
            "test-parser",
            [],
            [
                new FieldCandidate("merchant_name", merchantName, 1, "invoice-profile"),
                new FieldCandidate("invoice_number", invoiceNumber, 1, "invoice-profile"),
                new FieldCandidate("total_minor_units", totalMinorUnits, 1, "invoice-profile"),
            ],
            false);

    private sealed class FixedDocumentProcessor(DocumentAnalysis analysis) : IDocumentProcessor
    {
        public Task<DocumentAnalysis> AnalyzeAsync(
            DocumentJob job,
            CancellationToken cancellationToken = default) => Task.FromResult(analysis);
    }

    private sealed class SequenceDocumentProcessor(params DocumentAnalysis[] analyses) : IDocumentProcessor
    {
        private readonly Queue<DocumentAnalysis> _analyses = new(analyses);

        public Task<DocumentAnalysis> AnalyzeAsync(
            DocumentJob job,
            CancellationToken cancellationToken = default) => Task.FromResult(_analyses.Dequeue());
    }
}
