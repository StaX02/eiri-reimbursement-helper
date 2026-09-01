using Eiri.Reimbursement.Core.Documents;
using Eiri.Reimbursement.Core.Invoices;
using Eiri.Reimbursement.Core.Materials;
using Eiri.Reimbursement.Core.Orders;

namespace Eiri.Reimbursement.Core;

public interface IReimbursementWorkspace
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<OrderId> CreateOrderAsync(
        CreateOrderCommand command,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderListItem>> SearchOrdersAsync(
        OrderQuery query,
        CancellationToken cancellationToken = default);

    Task SetMilestoneAsync(
        SetMilestoneCommand command,
        CancellationToken cancellationToken = default);

    Task SetMilestonesAsync(
        IReadOnlyList<SetMilestoneCommand> commands,
        CancellationToken cancellationToken = default);

    Task UpdateOrderPlatformAsync(
        UpdateOrderPlatformCommand command,
        CancellationToken cancellationToken = default);

    Task<ImportMaterialsResult> ImportMaterialsAsync(
        ImportMaterialsCommand command,
        CancellationToken cancellationToken = default);

    Task<OrderDetail?> GetOrderAsync(
        OrderId orderId,
        CancellationToken cancellationToken = default);

    Task UpdateInvoiceAsync(
        UpdateInvoiceCommand command,
        CancellationToken cancellationToken = default);

    Task DeleteOrderAsync(
        OrderId orderId,
        CancellationToken cancellationToken = default);

    Task<DocumentAnalysis> AnalyzeInvoiceAsync(
        InvoiceId invoiceId,
        CancellationToken cancellationToken = default);
}
