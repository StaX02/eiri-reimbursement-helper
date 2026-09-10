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
