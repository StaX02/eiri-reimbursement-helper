using Eiri.Reimbursement.Core.Invoices;
using Eiri.Reimbursement.Core.Orders;

namespace Eiri.Reimbursement.Core.Materials;

public readonly record struct ManagedFileId(Guid Value)
{
    public static ManagedFileId New() => new(Guid.NewGuid());

    public static ManagedFileId Parse(string value) => new(Guid.Parse(value));

    public override string ToString() => Value.ToString("D");
}

public enum ManagedFileRole
{
    OrderScreenshot = 1,
    InvoicePdf = 2,
}

public enum MaterialImportOutcome
{
    Imported = 1,
    Duplicate = 2,
    Rejected = 3,
}

public sealed record ImportMaterialsCommand(
    OrderId OrderId,
    IReadOnlyList<string> SourcePaths);

public sealed record ManagedMaterial(
    ManagedFileId Id,
    ManagedFileRole Role,
    string OriginalFileName,
    string ManagedPath,
    string MediaType,
    long ByteLength,
    string Sha256,
    string ProcessingState,
    DateTimeOffset ImportedAt);

public sealed record MaterialImportItem(
    string SourcePath,
    MaterialImportOutcome Outcome,
    ManagedMaterial? Material,
    string? Message);

public sealed record ImportMaterialsResult(IReadOnlyList<MaterialImportItem> Items)
{
    public int ImportedCount => Items.Count(item => item.Outcome == MaterialImportOutcome.Imported);
}

public sealed record OrderDetail(
    OrderId Id,
    OrderPlatform Platform,
    string? ExternalOrderNumber,
    string? Notes,
    IReadOnlyList<ManagedMaterial> Materials,
    IReadOnlyList<InvoiceDetail> Invoices);
