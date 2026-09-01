using Eiri.Reimbursement.Core.Orders;

namespace Eiri.Reimbursement.Core.Tests;

public sealed class OrderListItemTests
{
    [Fact]
    public void ExposesSignedMinorUnitsAsCurrencyAmount()
    {
        OrderListItem order = CreateOrder(totalMinorUnits: 12_345);

        Assert.Equal(123.45m, order.TotalAmount);
    }

    [Fact]
    public void ShowsPlaceholderWhenInvoiceFieldsAreEmpty()
    {
        OrderListItem order = CreateOrder(totalMinorUnits: 0);

        Assert.Equal("待提取", order.MerchantDisplay);
        Assert.Equal("待提取", order.ProductDisplay);
        Assert.Equal("待提取", order.InvoiceNumberDisplay);
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
            null,
            null,
            null,
            DateTimeOffset.UtcNow);
}
