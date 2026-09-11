using Eiri.Reimbursement.Core;
using Eiri.Reimbursement.Core.DingTalk;
using Eiri.Reimbursement.Core.Export;
using Eiri.Reimbursement.Core.Materials;
using Eiri.Reimbursement.Core.Reimbursements;

namespace Eiri.Reimbursement.Infrastructure.Export;

public sealed class ApprovedReimbursementExporter(
    IReimbursementWorkspace workspace,
    IApprovedReimbursementDetailsClient detailsClient,
    IReimbursementPdfWriter pdfWriter,
    string libraryRoot) : IApprovedReimbursementExporter
{
    public async Task ExportAsync(Guid reimbursementId, string destinationPath, CancellationToken cancellationToken = default, bool overwrite = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        string destination = Path.GetFullPath(destinationPath);
        string managedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(libraryRoot));
        if (destination.Equals(managedRoot, StringComparison.OrdinalIgnoreCase)
            || destination.StartsWith(managedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("请选择受管资料库之外的 PDF 保存位置，以保留原始材料。");
        if (workspace is not IReimbursementFormWorkspace forms || workspace is not IDingTalkConnectionStore connections)
            throw new InvalidOperationException("报销单 PDF 导出不可用。");
        var detail = await forms.GetReimbursementAsync(reimbursementId, cancellationToken)
            ?? throw new InvalidOperationException("报销单已不存在。");
        var form = detail.Form;
        if (form.SubmittedAt is null || form.DingTalkApprovalStatus != "COMPLETED" || form.DingTalkApprovalResult != "agree"
            || string.IsNullOrWhiteSpace(form.DingTalkInstanceId))
            throw new InvalidOperationException("仅流程为“已同意”的报销单可以导出审批 PDF。");
        var connection = await connections.GetDingTalkConnectionAsync(cancellationToken);
        if (connection is null || string.IsNullOrWhiteSpace(connection.AccessToken) || connection.ExpiresAt is not { } expiry || expiry <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("请先通过“钉钉 → 连接接口”更新连接，再导出审批 PDF。");
        var data = await detailsClient.GetApprovedPrintDataAsync(connection.AccessToken, form.DingTalkInstanceId, cancellationToken);
        List<string> paths = [];
        foreach (var orderId in form.OrderIds)
        {
            var order = await workspace.GetOrderAsync(orderId, cancellationToken)
                ?? throw new InvalidOperationException("关联订单已不存在，请刷新后重试。");
            paths.AddRange(order.Materials.Where(m => m.Role == ManagedFileRole.OrderScreenshot).Select(m => m.ManagedPath));
        }
        if (paths.Concat(detail.Attachments.Select(f => f.ManagedPath)).Any(p => Path.GetFullPath(p).Equals(destination, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("请选择资料库原始材料之外的 PDF 保存路径。");
        string temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".eiri-{Guid.NewGuid():N}.pdf");
        try
        {
            await pdfWriter.WriteAsync(temporary, data, paths, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            string candidate = destination;
            for (int suffix = 2; ; suffix++)
            {
                try { File.Move(temporary, candidate, overwrite); break; }
                catch (IOException) when (!overwrite && (File.Exists(candidate) || Directory.Exists(candidate)))
                {
                    candidate = Path.Combine(Path.GetDirectoryName(destination)!, $"{Path.GetFileNameWithoutExtension(destination)}-{suffix}.pdf");
                }
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
