using System.Text;
using Eiri.Reimbursement.Core;
using Eiri.Reimbursement.Core.Documents;
using Eiri.Reimbursement.Core.Export;
using Eiri.Reimbursement.Core.Invoices;
using Eiri.Reimbursement.Core.Materials;
using Eiri.Reimbursement.Core.Orders;
using Eiri.Reimbursement.Infrastructure.Export;
using Eiri.Reimbursement.Infrastructure.Sqlite;

namespace Eiri.Reimbursement.Infrastructure.Tests;

public sealed class ReimbursementBatchExporterTests : IAsyncLifetime
{
    private readonly string _testRoot = Path.Combine(
        Path.GetTempPath(),
        "eiri-export-tests",
        Guid.NewGuid().ToString("N"));

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task ExportAsyncCreatesAllReimbursementFilesForSelectedOrders()
    {
        string libraryRoot = Path.Combine(_testRoot, "library");
        string destinationRoot = Path.Combine(_testRoot, "export");
        SqliteReimbursementWorkspace workspace = new(libraryRoot);
        await workspace.InitializeAsync();
        OrderId firstOrderId = await CreateOrderWithInvoiceAsync(
            workspace,
            "invoice-one.pdf",
            "京东自营",
            "25312000000000123456",
            15_990,
            "机械键盘");
        OrderId secondOrderId = await CreateOrderWithInvoiceAsync(
            workspace,
            "invoice-two.pdf",
            "文具/商店",
            "25312000000000654321",
            2_050,
            "签字笔");
        string supportingPath = Path.Combine(_testRoot, "order-detail.png");
        await File.WriteAllBytesAsync(
            supportingPath,
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        await workspace.ImportMaterialsAsync(new ImportMaterialsCommand(
            firstOrderId,
            [supportingPath],
            ManagedFileRole.OrderScreenshot));
        IReimbursementBatchExporter exporter = new ReimbursementBatchExporter(
            workspace,
            new FixedPdfPageRenderer(),
            () => new DateTimeOffset(2026, 9, 1, 12, 34, 56, TimeSpan.FromHours(8)));

        ExportBatchResult result = await exporter.ExportAsync(
            new ExportBatchCommand([firstOrderId, secondOrderId], destinationRoot));

        Assert.Equal(2, result.OrderCount);
        Assert.Equal(2, result.InvoiceCount);
        Assert.Equal(3, result.InvoiceImageCount);
        Assert.True(File.Exists(Path.Combine(
            destinationRoot,
            "发票图片",
            "25312000000000123456-159.90-第1页.png")));
        Assert.True(File.Exists(Path.Combine(
            destinationRoot,
            "发票图片",
            "25312000000000123456-159.90-第2页.png")));
        Assert.True(File.Exists(Path.Combine(
            destinationRoot,
            "发票图片",
            "25312000000000654321-20.50.png")));
        Assert.True(File.Exists(Path.Combine(
            destinationRoot,
            "发票原件",
            "25312000000000123456-159.90-京东自营-机械键盘.pdf")));
        Assert.True(File.Exists(Path.Combine(
            destinationRoot,
            "发票原件",
            "25312000000000654321-20.50-文具_商店-签字笔.pdf")));
        Assert.True(File.Exists(Path.Combine(
            destinationRoot,
            "报销辅助材料",
            "机械键盘-159.90-辅助材料-1.png")));
        string csvPath = Assert.Single(Directory.GetFiles(
            destinationRoot,
            "发票导出-20260901-123456.csv"));
        string csv = await File.ReadAllTextAsync(csvPath, Encoding.UTF8);
        Assert.Equal(
            "总金额,发票号\r\n159.90,25312000000000123456\r\n20.50,25312000000000654321\r\n",
            csv.TrimStart('\uFEFF'));
    }

    [Fact]
    public async Task SupportingMaterialUsesOrderProductSummaryForMultipleInvoices()
    {
        string libraryRoot = Path.Combine(_testRoot, "library");
        string destinationRoot = Path.Combine(_testRoot, "export");
        IReimbursementWorkspace workspace = new SqliteReimbursementWorkspace(libraryRoot);
        await workspace.InitializeAsync();
        OrderId orderId = await workspace.CreateOrderAsync(new CreateOrderCommand(OrderPlatform.JD));
        await AddInvoiceAsync(
            workspace,
            orderId,
            "multi-one.pdf",
            "商家甲",
            "10000000000000000001",
            15_990,
            ["显示器", "支架"]);
        await AddInvoiceAsync(
            workspace,
            orderId,
            "multi-two.pdf",
            "商家乙",
            "10000000000000000002",
            2_050,
            ["鼠标"]);
        string supportingPath = Path.Combine(_testRoot, "order-detail.png");
        await File.WriteAllBytesAsync(
            supportingPath,
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        await workspace.ImportMaterialsAsync(new ImportMaterialsCommand(
            orderId,
            [supportingPath],
            ManagedFileRole.OrderScreenshot));
        IReimbursementBatchExporter exporter = new ReimbursementBatchExporter(
            workspace,
            new FixedPdfPageRenderer());

        await exporter.ExportAsync(new ExportBatchCommand([orderId], destinationRoot));

        Assert.True(File.Exists(Path.Combine(
            destinationRoot,
            "报销辅助材料",
            "显示器等-180.40-辅助材料-1.png")));
    }

    private async Task<OrderId> CreateOrderWithInvoiceAsync(
        IReimbursementWorkspace workspace,
        string fileName,
        string merchantName,
        string invoiceNumber,
        long totalMinorUnits,
        string productName)
    {
        OrderId orderId = await workspace.CreateOrderAsync(new CreateOrderCommand(OrderPlatform.JD));
        await AddInvoiceAsync(
            workspace,
            orderId,
            fileName,
            merchantName,
            invoiceNumber,
            totalMinorUnits,
            [productName]);
        return orderId;
    }

    private async Task AddInvoiceAsync(
        IReimbursementWorkspace workspace,
        OrderId orderId,
        string fileName,
        string merchantName,
        string invoiceNumber,
        long totalMinorUnits,
        IReadOnlyList<string> productNames)
    {
        string invoicePath = Path.Combine(_testRoot, fileName);
        await File.WriteAllBytesAsync(
            invoicePath,
            Encoding.ASCII.GetBytes($"%PDF-1.7 {fileName}"));
        await workspace.ImportMaterialsAsync(new ImportMaterialsCommand(
            orderId,
            [invoicePath],
            ManagedFileRole.InvoicePdf));
        InvoiceDetail invoice = Assert.IsType<OrderDetail>(
                await workspace.GetOrderAsync(orderId))
            .Invoices
            .Single(candidate => candidate.OriginalFileName == fileName);
        await workspace.UpdateInvoiceAsync(new UpdateInvoiceCommand(
            invoice.Id,
            merchantName,
            invoiceNumber,
            totalMinorUnits,
            productNames.Select(name => new InvoiceLineCorrection(name)).ToArray()));
    }

    private sealed class FixedPdfPageRenderer : IPdfPageRenderer
    {
        public async Task<IReadOnlyList<string>> RenderAsync(
            string pdfPath,
            string destinationDirectory,
            CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(destinationDirectory);
            List<string> paths = [];
            string pdfContent = await File.ReadAllTextAsync(pdfPath, cancellationToken);
            int pageCount = pdfContent.Contains("invoice-one", StringComparison.Ordinal) ? 2 : 1;
            for (int page = 1; page <= pageCount; page++)
            {
                string path = Path.Combine(destinationDirectory, $"page-{page}.png");
                await File.WriteAllBytesAsync(path, [0x89, 0x50, 0x4E, 0x47], cancellationToken);
                paths.Add(path);
            }

            return paths;
        }
    }
}
