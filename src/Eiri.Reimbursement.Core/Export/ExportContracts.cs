using Eiri.Reimbursement.Core.Orders;

namespace Eiri.Reimbursement.Core.Export;

public sealed record ExportBatchCommand(
    IReadOnlyList<OrderId> OrderIds,
    string DestinationDirectory);

public sealed record ExportBatchResult(
    int OrderCount,
    int InvoiceCount,
    int InvoiceImageCount,
    int SupportingMaterialCount,
    string CsvPath);

public interface IReimbursementBatchExporter
{
    Task<ExportBatchResult> ExportAsync(
        ExportBatchCommand command,
        CancellationToken cancellationToken = default);
}
