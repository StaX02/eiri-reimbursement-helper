using Eiri.Reimbursement.Core.Materials;

namespace Eiri.Reimbursement.Core.Invoices;

public readonly record struct InvoiceId(Guid Value)
{
    public static InvoiceId Parse(string value) => new(Guid.Parse(value));

    public override string ToString() => Value.ToString("D");
}

public sealed record InvoiceLineDetail(
    int Sequence,
    string Name,
    long? AmountMinorUnits,
    bool IsEffective);

public sealed record InvoiceDetail(
    InvoiceId Id,
    ManagedFileId ManagedFileId,
    string OriginalFileName,
    string MerchantName,
    string InvoiceNumber,
    long TotalMinorUnits,
    bool NeedsReview,
    IReadOnlyList<InvoiceLineDetail> Lines)
{
    public decimal TotalAmount => TotalMinorUnits / 100m;

    public string PrimaryProductDisplay
    {
        get
        {
            string[] names = Lines
                .Where(line => line.IsEffective && !string.IsNullOrWhiteSpace(line.Name))
                .OrderBy(line => line.Sequence)
                .Select(line => line.Name)
                .ToArray();
            return names.Length switch
            {
                0 => string.Empty,
                1 => names[0],
                _ => $"{names[0]}等{names.Length - 1}条",
            };
        }
    }
}

public sealed record InvoiceLineCorrection(
    string Name,
    long? AmountMinorUnits = null,
    bool IsEffective = true);

public sealed record UpdateInvoiceCommand(
    InvoiceId InvoiceId,
    string MerchantName,
    string InvoiceNumber,
    long TotalMinorUnits,
    IReadOnlyList<InvoiceLineCorrection> Lines);
