namespace Eiri.Reimbursement.Core.Orders;

public sealed record CreateOrderCommand(
    OrderPlatform Platform,
    string? ExternalOrderNumber = null,
    string? Notes = null);

public sealed record SetMilestoneCommand(
    OrderId OrderId,
    Milestone Milestone,
    DateTimeOffset? OccurredAt);

public sealed record OrderQuery(
    string? SearchText = null,
    OrderPlatform? Platform = null,
    int Offset = 0,
    int Limit = 100);

public sealed record OrderListItem(
    OrderId Id,
    OrderPlatform Platform,
    string? ExternalOrderNumber,
    IReadOnlyList<string> MerchantNames,
    IReadOnlyList<string> ProductNames,
    long TotalMinorUnits,
    IReadOnlyList<string> InvoiceNumbers,
    DateTimeOffset? ExportedAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? RefundedAt,
    DateTimeOffset CreatedAt)
{
    public decimal TotalAmount => TotalMinorUnits / 100m;

    public string MerchantDisplay => JoinOrPlaceholder(MerchantNames);

    public string ProductDisplay => JoinOrPlaceholder(ProductNames);

    public string InvoiceNumberDisplay => JoinOrPlaceholder(InvoiceNumbers);

    private static string JoinOrPlaceholder(IReadOnlyList<string> values) =>
        values.Count == 0 ? "待提取" : string.Join("、", values);
}
