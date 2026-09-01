using System.IO;
using Eiri.Reimbursement.Core.Documents;
using Eiri.Reimbursement.Core.Materials;
using Eiri.Reimbursement.Core.Orders;
using Eiri.Reimbursement.Desktop.ViewModels;
using Eiri.Reimbursement.Infrastructure.Sqlite;

namespace Eiri.Reimbursement.Desktop.Tests;

public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly string _libraryRoot = Path.Combine(
        Path.GetTempPath(),
        "eiri-desktop-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CreatingOrderUsesSelectedPlatform()
    {
        SqliteReimbursementWorkspace workspace = new(_libraryRoot);
        await workspace.InitializeAsync();
        MainWindowViewModel viewModel = new(workspace);
        await viewModel.LoadAsync();

        Assert.Equal(
            ["淘宝", "京东", "其他平台"],
            viewModel.PlatformOptions.Select(option => option.DisplayName));
        viewModel.SelectedPlatform = viewModel.PlatformOptions.Single(
            option => option.Value == OrderPlatform.JD);
        await viewModel.CreateOrderCommand.ExecuteAsync(null);

        OrderListItem order = Assert.Single(viewModel.Orders);
        Assert.Equal(OrderPlatform.JD, order.Platform);
    }

    [Fact]
    public async Task LoadingExistingOrdersPreservesEmptySelection()
    {
        SqliteReimbursementWorkspace workspace = new(_libraryRoot);
        await workspace.InitializeAsync();
        await workspace.CreateOrderAsync(new CreateOrderCommand(OrderPlatform.Taobao));
        MainWindowViewModel viewModel = new(workspace);

        await viewModel.LoadAsync();

        Assert.Single(viewModel.Orders);
        Assert.Null(viewModel.SelectedOrder);
    }

    [Fact]
    public async Task ImportingInvoicesAnalyzesEachFileAndRefreshesOrderSummary()
    {
        SequenceDocumentProcessor processor = new(
            Analysis("商家甲", "10000000000000000001", "12345"),
            Analysis("商家甲", "10000000000000000001", "20000"));
        SqliteReimbursementWorkspace workspace = new(_libraryRoot, processor);
        await workspace.InitializeAsync();
        await workspace.CreateOrderAsync(new CreateOrderCommand(OrderPlatform.Other));
        string firstPath = Path.Combine(_libraryRoot, "invoice-a.pdf");
        string secondPath = Path.Combine(_libraryRoot, "invoice-b.pdf");
        await File.WriteAllBytesAsync(firstPath, "%PDF-1.7 invoice a"u8.ToArray());
        await File.WriteAllBytesAsync(secondPath, "%PDF-1.7 invoice b"u8.ToArray());
        MainWindowViewModel viewModel = new(workspace);
        await viewModel.LoadAsync();
        viewModel.SelectedOrder = Assert.Single(viewModel.Orders);

        await viewModel.ImportFilesAsync(
            [firstPath, secondPath],
            ManagedFileRole.InvoicePdf);

        OrderListItem order = Assert.Single(viewModel.Orders);
        Assert.Equal("多个商家", order.MerchantDisplay);
        Assert.Equal(["商家甲"], order.MerchantOptions);
        Assert.Equal(323.45m, order.TotalAmount);
        Assert.Equal(
            ["10000000000000000001", "10000000000000000001"],
            order.InvoiceNumbers);
        Assert.Equal(2, viewModel.Invoices.Count);
        Assert.All(viewModel.Materials, material => Assert.Equal("已处理", material.ProcessingStateDisplay));
    }

    public void Dispose()
    {
        if (Directory.Exists(_libraryRoot))
        {
            Directory.Delete(_libraryRoot, recursive: true);
        }
    }

    private static DocumentAnalysis Analysis(
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

    private sealed class SequenceDocumentProcessor(params DocumentAnalysis[] analyses) : IDocumentProcessor
    {
        private readonly Queue<DocumentAnalysis> _analyses = new(analyses);

        public Task<DocumentAnalysis> AnalyzeAsync(
            DocumentJob job,
            CancellationToken cancellationToken = default) => Task.FromResult(_analyses.Dequeue());
    }
}
