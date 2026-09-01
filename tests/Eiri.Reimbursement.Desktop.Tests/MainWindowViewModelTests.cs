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
    public async Task DeletingMultipleOrdersRemovesOnlySelectedOrders()
    {
        SqliteReimbursementWorkspace workspace = new(_libraryRoot);
        await workspace.InitializeAsync();
        OrderId firstOrderId = await workspace.CreateOrderAsync(
            new CreateOrderCommand(OrderPlatform.Taobao));
        OrderId secondOrderId = await workspace.CreateOrderAsync(
            new CreateOrderCommand(OrderPlatform.JD));
        OrderId remainingOrderId = await workspace.CreateOrderAsync(
            new CreateOrderCommand(OrderPlatform.Other));
        MainWindowViewModel viewModel = new(workspace);
        await viewModel.LoadAsync();
        viewModel.SelectedOrder = viewModel.Orders.Single(order => order.Id == firstOrderId);

        await viewModel.DeleteOrdersAsync([firstOrderId, secondOrderId]);

        OrderListItem remainingOrder = Assert.Single(viewModel.Orders);
        Assert.Equal(remainingOrderId, remainingOrder.Id);
        Assert.Null(viewModel.SelectedOrder);
        Assert.Empty(viewModel.Materials);
        Assert.Empty(viewModel.Invoices);
        Assert.Equal("已删除 2 个订单及其受管材料。", viewModel.StatusMessage);
    }

    [Theory]
    [InlineData(Milestone.Submitted)]
    [InlineData(Milestone.Refunded)]
    public async Task MarkingMultipleOrdersMilestoneUpdatesEverySelectedOrder(Milestone milestone)
    {
        SqliteReimbursementWorkspace workspace = new(_libraryRoot);
        await workspace.InitializeAsync();
        OrderId firstOrderId = await workspace.CreateOrderAsync(
            new CreateOrderCommand(OrderPlatform.Taobao));
        OrderId secondOrderId = await workspace.CreateOrderAsync(
            new CreateOrderCommand(OrderPlatform.JD));
        OrderId unchangedOrderId = await workspace.CreateOrderAsync(
            new CreateOrderCommand(OrderPlatform.Other));
        MainWindowViewModel viewModel = new(workspace);
        await viewModel.LoadAsync();
        viewModel.SelectedOrder = viewModel.Orders.Single(order => order.Id == firstOrderId);

        await viewModel.SetOrdersMilestoneAsync(
            [firstOrderId, secondOrderId],
            milestone,
            isReached: true);

        OrderListItem[] updatedOrders = viewModel.Orders
            .Where(order => order.Id == firstOrderId || order.Id == secondOrderId)
            .ToArray();
        Assert.Equal(2, updatedOrders.Length);
        OrderListItem unchangedOrder = viewModel.Orders.Single(order => order.Id == unchangedOrderId);
        if (milestone == Milestone.Submitted)
        {
            Assert.All(updatedOrders, order => Assert.NotNull(order.SubmittedAt));
            Assert.Null(unchangedOrder.SubmittedAt);
            Assert.True(viewModel.IsSelectedOrderSubmitted);
            Assert.Equal("已将 2 个订单设为已提交。", viewModel.StatusMessage);
        }
        else
        {
            Assert.All(updatedOrders, order => Assert.NotNull(order.RefundedAt));
            Assert.Null(unchangedOrder.RefundedAt);
            Assert.True(viewModel.IsSelectedOrderRefunded);
            Assert.Equal("已将 2 个订单设为已返款。", viewModel.StatusMessage);
        }
    }

    [Fact]
    public async Task ClearingSubmissionAndRefundStatusOnlyUpdatesSelectedOrders()
    {
        SqliteReimbursementWorkspace workspace = new(_libraryRoot);
        await workspace.InitializeAsync();
        OrderId selectedOrderId = await workspace.CreateOrderAsync(
            new CreateOrderCommand(OrderPlatform.Taobao));
        OrderId unchangedOrderId = await workspace.CreateOrderAsync(
            new CreateOrderCommand(OrderPlatform.JD));
        DateTimeOffset occurredAt = DateTimeOffset.UtcNow;
        foreach (OrderId orderId in new[] { selectedOrderId, unchangedOrderId })
        {
            await workspace.SetMilestoneAsync(
                new SetMilestoneCommand(orderId, Milestone.Submitted, occurredAt));
            await workspace.SetMilestoneAsync(
                new SetMilestoneCommand(orderId, Milestone.Refunded, occurredAt));
        }

        MainWindowViewModel viewModel = new(workspace);
        await viewModel.LoadAsync();
        viewModel.SelectedOrder = viewModel.Orders.Single(order => order.Id == selectedOrderId);

        await viewModel.ClearOrdersSubmissionAndRefundAsync([selectedOrderId]);

        OrderListItem clearedOrder = viewModel.Orders.Single(order => order.Id == selectedOrderId);
        OrderListItem unchangedOrder = viewModel.Orders.Single(order => order.Id == unchangedOrderId);
        Assert.Null(clearedOrder.SubmittedAt);
        Assert.Null(clearedOrder.RefundedAt);
        Assert.NotNull(unchangedOrder.SubmittedAt);
        Assert.NotNull(unchangedOrder.RefundedAt);
        Assert.False(viewModel.IsSelectedOrderSubmitted);
        Assert.False(viewModel.IsSelectedOrderRefunded);
        Assert.Equal("已清空 1 个订单的提交及返款状态。", viewModel.StatusMessage);
    }

    [Fact]
    public async Task UpdatingSelectedOrderStatusPersistsEachEditedValueImmediately()
    {
        SqliteReimbursementWorkspace workspace = new(_libraryRoot);
        await workspace.InitializeAsync();
        OrderId orderId = await workspace.CreateOrderAsync(
            new CreateOrderCommand(OrderPlatform.Taobao));
        await workspace.SetMilestoneAsync(
            new SetMilestoneCommand(orderId, Milestone.Submitted, DateTimeOffset.UtcNow));
        MainWindowViewModel viewModel = new(workspace);
        await viewModel.LoadAsync();
        viewModel.SelectedOrder = Assert.Single(viewModel.Orders);

        await viewModel.SetOrdersMilestoneAsync(
            [orderId],
            Milestone.Submitted,
            isReached: false);
        await viewModel.SetOrdersMilestoneAsync(
            [orderId],
            Milestone.Refunded,
            isReached: true);

        OrderListItem updatedOrder = Assert.Single(viewModel.Orders);
        Assert.Null(updatedOrder.SubmittedAt);
        Assert.NotNull(updatedOrder.RefundedAt);
        Assert.False(viewModel.IsSelectedOrderSubmitted);
        Assert.True(viewModel.IsSelectedOrderRefunded);
        Assert.Equal("已将 1 个订单设为已返款。", viewModel.StatusMessage);
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

    [Fact]
    public async Task SavingInvoiceEditsAlsoUpdatesOrderPlatformAndProductNames()
    {
        SqliteReimbursementWorkspace workspace = new(_libraryRoot);
        await workspace.InitializeAsync();
        OrderId orderId = await workspace.CreateOrderAsync(
            new CreateOrderCommand(OrderPlatform.Other));
        string invoicePath = Path.Combine(_libraryRoot, "editable-invoice.pdf");
        await File.WriteAllBytesAsync(invoicePath, "%PDF-1.7 editable invoice"u8.ToArray());
        await workspace.ImportMaterialsAsync(
            new ImportMaterialsCommand(orderId, [invoicePath], ManagedFileRole.InvoicePdf));
        MainWindowViewModel viewModel = new(workspace);
        await viewModel.LoadAsync();
        viewModel.SelectedOrder = Assert.Single(viewModel.Orders);
        await WaitUntilAsync(() => viewModel.SelectedInvoice is not null);
        viewModel.SelectedOrderPlatform = viewModel.PlatformOptions.Single(
            option => option.Value == OrderPlatform.JD);
        viewModel.SelectedInvoice!.MerchantName = "京东自营";
        viewModel.SelectedInvoice.InvoiceNumber = "25312000000000123456";
        viewModel.SelectedInvoice.AmountText = "159.90";
        viewModel.SelectedInvoice.ProductNamesText = "机械键盘\n键帽";

        await viewModel.SaveInvoiceCommand.ExecuteAsync(null);

        Assert.Equal(OrderPlatform.JD, Assert.IsType<OrderListItem>(viewModel.SelectedOrder).Platform);
        Assert.Equal("机械键盘等1条", viewModel.SelectedOrder.ProductDisplay);
        Assert.Equal(OrderPlatform.JD, Assert.IsType<OrderDetail>(
            await workspace.GetOrderAsync(orderId)).Platform);
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

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (!condition() && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.True(condition(), "The expected view-model state was not reached in time.");
    }

    private sealed class SequenceDocumentProcessor(params DocumentAnalysis[] analyses) : IDocumentProcessor
    {
        private readonly Queue<DocumentAnalysis> _analyses = new(analyses);

        public Task<DocumentAnalysis> AnalyzeAsync(
            DocumentJob job,
            CancellationToken cancellationToken = default) => Task.FromResult(_analyses.Dequeue());
    }
}
