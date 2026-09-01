namespace Eiri.Reimbursement.Core.Orders;

public readonly record struct OrderId(Guid Value)
{
    public static OrderId New() => new(Guid.NewGuid());

    public static OrderId Parse(string value) => new(Guid.Parse(value));

    public override string ToString() => Value.ToString("D");
}
