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

    [Fact]
    public void MultipleMerchantsShowTheFirstMerchantAsASummary()
    {
        OrderListItem order = new(
            OrderId.New(),
            OrderPlatform.Other,
            null,
            ["商家甲", "商家乙", "商家甲"],
            [],
            32_145,
            ["10000000000000000001", "10000000000000000002"],
            2,
            null,
            null,
            null,
            DateTimeOffset.UtcNow);

        Assert.Equal("商家甲等", order.MerchantDisplay);
        Assert.Equal(["商家甲", "商家乙"], order.MerchantOptions);
        Assert.Equal(321.45m, order.TotalAmount);
        Assert.Equal(2, order.InvoiceNumbers.Count);
    }

    [Fact]
    public void DisplaysPlatformAndCreationDateForOrderList()
    {
        DateTime localDateTime = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Unspecified);
        DateTimeOffset createdAt = new(localDateTime, TimeZoneInfo.Local.GetUtcOffset(localDateTime));
        OrderListItem order = new(
            OrderId.New(),
            OrderPlatform.JD,
            null,
            [],
            [],
            0,
            [],
            0,
            null,
            null,
            null,
            createdAt);

        Assert.Equal("京东", order.PlatformDisplay);
        Assert.Equal("2026-09-01", order.CreatedDateDisplay);
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
