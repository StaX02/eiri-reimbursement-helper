using Eiri.Reimbursement.Core;
using Eiri.Reimbursement.Core.DingTalk;
using Eiri.Reimbursement.Core.Documents;
using Eiri.Reimbursement.Core.Materials;
using Eiri.Reimbursement.Core.Reimbursements;

namespace Eiri.Reimbursement.Infrastructure.DingTalk;

public sealed class DingTalkInvoiceImagePreparer(IReimbursementWorkspace workspace, IPdfPageRenderer? renderer) : IDingTalkInvoiceImages
{
    public async Task<DingTalkInvoiceImages> PrepareAsync(ReimbursementForm form, string destinationDirectory, CancellationToken cancellationToken = default)
    {
        List<string> images = [];
        List<string> numbers = [];
        foreach (var orderId in form.OrderIds.Distinct())
        {
            var order = await workspace.GetOrderAsync(orderId, cancellationToken)
                ?? throw new InvalidOperationException("报销单关联订单已不存在，请刷新后重试。");
            numbers.AddRange(order.Invoices.Select(i => i.InvoiceNumber).Where(n => !string.IsNullOrWhiteSpace(n)));
            foreach (var material in order.Materials.Where(m => m.Role == ManagedFileRole.InvoicePdf))
            {
                if (renderer is null) throw new InvalidOperationException("发票转图组件不可用，请检查文档处理器安装。");
                var pages = await renderer.RenderAsync(material.ManagedPath,
                    Path.Combine(destinationDirectory, orderId.ToString(), material.Id.ToString()), cancellationToken);
                if (pages.Count == 0 || pages.Any(p => !File.Exists(p)))
                    throw new InvalidOperationException("发票转换没有生成完整图片，请检查发票文件后重试。");
                images.AddRange(pages);
            }
        }
        return new(images, numbers);
    }
}
