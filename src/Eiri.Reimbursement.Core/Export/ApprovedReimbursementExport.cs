namespace Eiri.Reimbursement.Core.Export;

public sealed record ReimbursementPrintField(string Name, string Value);
public sealed record ApprovedReimbursementPrintData(string Reimburser, IReadOnlyList<ReimbursementPrintField> Fields);

public interface IApprovedReimbursementDetailsClient
{
    Task<ApprovedReimbursementPrintData> GetApprovedPrintDataAsync(string accessToken, string instanceId, CancellationToken cancellationToken = default);
}

public interface IReimbursementPdfWriter
{
    Task WriteAsync(string destinationPath, ApprovedReimbursementPrintData data, IReadOnlyList<string> supportingMaterialPaths, CancellationToken cancellationToken = default);
}

public interface IApprovedReimbursementExporter
{
    Task ExportAsync(Guid reimbursementId, string destinationPath, CancellationToken cancellationToken = default, bool overwrite = false);
}
