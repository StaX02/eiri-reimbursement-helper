using System.Globalization;

namespace Eiri.Reimbursement.Core.Orders;

public sealed record CreateOrderCommand(
    OrderPlatform Platform,
    string? ExternalOrderNumber = null,
    string? Notes = null);

public sealed record SetMilestoneCommand(
    OrderId OrderId,
    Milestone Milestone,
    DateTimeOffset? OccurredAt);

public sealed record UpdateOrderPlatformCommand(
    OrderId OrderId,
    OrderPlatform Platform);

public sealed record OrderQuery(
    string? SearchText = null,
    OrderPlatform? Platform = null,
    int Offset = 0,
    int Limit = 100,
    bool? Archived = null);

public sealed record OrderListItem(
    OrderId Id,
    OrderPlatform Platform,
    string? ExternalOrderNumber,
    IReadOnlyList<string> MerchantNames,
    IReadOnlyList<string> ProductNames,
    long TotalMinorUnits,
    IReadOnlyList<string> InvoiceNumbers,
    int InvoiceCount,
    DateTimeOffset? ExportedAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? RefundedAt,
    DateTimeOffset CreatedAt,
    Guid? ReimbursementId = null,
    string? ReimbursementContent = null)
{
    public string ReimbursementDisplay => ReimbursementId is null ? "暂未绑定" : string.IsNullOrWhiteSpace(ReimbursementContent) ? "报销内容待填写" : ReimbursementContent;

    public decimal TotalAmount => TotalMinorUnits / 100m;

    public string PlatformDisplay => Platform.ToDisplayName();

    public string CreatedDateDisplay => CreatedAt
        .ToLocalTime()
        .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public IReadOnlyList<string> MerchantOptions => MerchantNames
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    public string MerchantDisplay => MerchantOptions.Count switch
    {
        0 => "待提取",
        1 => MerchantOptions[0],
        _ => $"{MerchantOptions[0]}等",
    };

    public string ProductDisplay => JoinOrPlaceholder(ProductNames);

    public string InvoiceNumberDisplay => InvoiceCount > 1
        ? "多张发票..."
        : InvoiceNumbers.FirstOrDefault() ?? "待提取";

    public string? InvoiceNumbersToolTip => InvoiceCount > 1
        ? InvoiceNumbers.Count == 0 ? "待提取" : string.Join(Environment.NewLine, InvoiceNumbers)
        : null;

    private static string JoinOrPlaceholder(IReadOnlyList<string> values) =>
        values.Count == 0 ? "待提取" : string.Join("、", values);
}
