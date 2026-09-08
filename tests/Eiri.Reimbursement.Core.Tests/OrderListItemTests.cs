using Eiri.Reimbursement.Core.Orders;

namespace Eiri.Reimbursement.Core.Tests;

public sealed class OrderListItemTests
{
    [Theory]
    [InlineData(12_345, 123.45)]
    [InlineData(-12_345, -123.45)]
    [InlineData(0, 0)]
    public void ExposesSignedMinorUnitsAsCurrencyAmount(long minorUnits, decimal expectedAmount)
    {
        OrderListItem order = CreateOrder(totalMinorUnits: minorUnits);

        Assert.Equal(expectedAmount, order.TotalAmount);
    }

    [Fact]
    public void SingleInvoiceDisplaysItsNumberWithoutToolTip()
    {
        OrderListItem order = CreateOrder(0) with { InvoiceCount = 1, InvoiceNumbers = ["1001"] };

        Assert.Equal("1001", order.InvoiceNumberDisplay);
        Assert.Null(order.InvoiceNumbersToolTip);
    }

    [Fact]
    public void MultipleInvoicesShowSummaryAndOneNumberPerToolTipLine()
    {
        OrderListItem order = CreateOrder(0) with { InvoiceCount = 2, InvoiceNumbers = ["1001", "1002"] };

        Assert.Equal("多张发票...", order.InvoiceNumberDisplay);
        Assert.Equal($"1001{Environment.NewLine}1002", order.InvoiceNumbersToolTip);
    }

    [Fact]
    public void MultipleInvoicesStillShowSummaryWhenOnlyOneNumberIsExtracted()
    {
        OrderListItem order = CreateOrder(0) with { InvoiceCount = 2, InvoiceNumbers = ["1001"] };

        Assert.Equal("多张发票...", order.InvoiceNumberDisplay);
        Assert.Equal("1001", order.InvoiceNumbersToolTip);
    }

    [Theory]
    [InlineData(0, "待提取", null)]
    [InlineData(1, "待提取", null)]
    [InlineData(2, "多张发票...", "待提取")]
    public void MissingNumbersRetainExtractionPlaceholder(int invoiceCount, string display, string? toolTip)
    {
        OrderListItem order = CreateOrder(0) with { InvoiceCount = invoiceCount };

        Assert.Equal(display, order.InvoiceNumberDisplay);
        Assert.Equal(toolTip, order.InvoiceNumbersToolTip);
    }

    private static OrderListItem CreateOrder(long totalMinorUnits) =>
        new(
            OrderId.New(),
            OrderPlatform.Other,
            null,
            [],
            [],
            totalMinorUnits,
            [],
            0,
            null,
            null,
            null,
            DateTimeOffset.UtcNow);
}
