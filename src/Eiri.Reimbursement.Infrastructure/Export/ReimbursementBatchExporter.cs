using System.Globalization;
using System.Text;
using Eiri.Reimbursement.Core;
using Eiri.Reimbursement.Core.Documents;
using Eiri.Reimbursement.Core.Export;
using Eiri.Reimbursement.Core.Invoices;
using Eiri.Reimbursement.Core.Materials;
using Eiri.Reimbursement.Core.Orders;
using Eiri.Reimbursement.Core.Reimbursements;

namespace Eiri.Reimbursement.Infrastructure.Export;

public sealed class ReimbursementBatchExporter(
    IReimbursementWorkspace workspace,
    IPdfPageRenderer pdfPageRenderer,
    Func<DateTimeOffset>? clock = null) : IReimbursementBatchExporter
{
    private readonly IReimbursementWorkspace _workspace = workspace;
    private readonly IPdfPageRenderer _pdfPageRenderer = pdfPageRenderer;
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.Now);

    public async Task<ExportBatchResult> ExportAsync(
        ExportBatchCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.DestinationDirectory);
        List<ReimbursementDetail> forms = [];
        foreach (Guid id in (command.ReimbursementIds ?? []).Distinct())
        {
            if (_workspace is not IReimbursementFormWorkspace formWorkspace) throw new InvalidOperationException("报销单导出不可用。");
            var form = await formWorkspace.GetReimbursementAsync(id, cancellationToken) ?? throw new KeyNotFoundException("报销单已不存在。");
            if (!form.Attachments.Any(file => Path.GetExtension(file.ManagedPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"报销单“{form.Form.ContentDisplay}”尚未添加 PDF，请先添加报销单文件。");
            forms.Add(form);
        }
        OrderId[] orderIds = command.OrderIds.Concat(forms.SelectMany(form => form.Form.OrderIds)).Distinct().ToArray();
        if (orderIds.Length == 0)
        {
            throw new ArgumentException("At least one order is required for export.", nameof(command));
        }

        List<OrderExportSnapshot> snapshots = [];
        foreach (OrderId orderId in orderIds)
        {
            OrderDetail detail = await _workspace.GetOrderAsync(orderId, cancellationToken)
                ?? throw new KeyNotFoundException($"Order '{orderId}' was not found.");
            snapshots.Add(new(detail, detail.Invoices.Sum(invoice => invoice.TotalMinorUnits)));
        }
        var boundOrderIds = forms.SelectMany(form => form.Form.OrderIds).ToHashSet();
        long totalMinorUnits = checked(forms.Sum(form => form.Form.TotalMinorUnits ??
            snapshots.Where(snapshot => form.Form.OrderIds.Contains(snapshot.Detail.Id)).Sum(snapshot => snapshot.TotalMinorUnits)) +
            snapshots.Where(snapshot => !boundOrderIds.Contains(snapshot.Detail.Id)).Sum(snapshot => snapshot.TotalMinorUnits));
        DateTimeOffset exportedAt = _clock();
        string destinationRoot = AvailablePath(Path.GetFullPath(command.DestinationDirectory),
            $"报销材料导出-{FormatAmount(totalMinorUnits)}-{exportedAt:yyyyMMdd-HHmmss}", "");
        Directory.CreateDirectory(destinationRoot);
        string invoiceImageDirectory = Path.Combine(destinationRoot, "发票图片");
        string invoiceOriginalDirectory = Path.Combine(destinationRoot, "发票原件");
        string supportingMaterialDirectory = Path.Combine(destinationRoot, "报销辅助材料");
        Directory.CreateDirectory(invoiceImageDirectory);
        Directory.CreateDirectory(invoiceOriginalDirectory);
        Directory.CreateDirectory(supportingMaterialDirectory);

        string printDirectory = Path.Combine(destinationRoot, "打印材料");
        Directory.CreateDirectory(printDirectory);
        int invoiceImageCount = 0;
        int supportingMaterialCount = 0;
        string renderRoot = Path.Combine(destinationRoot, $".eiri-render-{Guid.NewGuid():N}");
        try
        {
            foreach (var form in forms)
            {
                string directory = Path.Combine(destinationRoot, "报销单图片");
                Directory.CreateDirectory(directory);
                foreach (var file in form.Attachments.Where(file => Path.GetExtension(file.ManagedPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase)))
                {
                    var pages = await _pdfPageRenderer.RenderFirstPageAsync(file.ManagedPath, Path.Combine(renderRoot, file.Id.ToString()), cancellationToken);
                    if (pages.Count != 1) throw new InvalidDataException("报销单 PDF 首页转换失败。");
                    string baseName = FileNamePart(form.Form.Content, "未填写报销内容") + "-" + FileNamePart(Path.GetFileNameWithoutExtension(file.OriginalFileName), "报销单") + "-第1页";
                    File.Copy(pages[0], AvailablePath(printDirectory, baseName, ".png"));
                    File.Move(pages[0], AvailablePath(directory, baseName, ".png"));
                }
            }
            foreach (OrderExportSnapshot snapshot in snapshots)
            {
                OrderDetail detail = snapshot.Detail;
                long orderTotalMinorUnits = snapshot.TotalMinorUnits;

                foreach (InvoiceDetail invoice in detail.Invoices)
                {
                    ManagedMaterial material = detail.Materials.Single(
                        candidate => candidate.Id == invoice.ManagedFileId);
                    string amount = FormatAmount(invoice.TotalMinorUnits);
                    string invoiceNumber = FileNamePart(invoice.InvoiceNumber, "未填写发票号");
                    string imageBaseName = $"{invoiceNumber}-{amount}";
                    string invoiceRenderDirectory = Path.Combine(renderRoot, invoice.Id.ToString());
                    IReadOnlyList<string> renderedPages = await _pdfPageRenderer.RenderAsync(
                        material.ManagedPath,
                        invoiceRenderDirectory,
                        cancellationToken);
                    if (renderedPages.Count == 0)
                    {
                        throw new InvalidDataException($"Invoice '{invoice.OriginalFileName}' produced no images.");
                    }

                    for (int pageIndex = 0; pageIndex < renderedPages.Count; pageIndex++)
                    {
                        string pageSuffix = renderedPages.Count == 1 ? string.Empty : $"-第{pageIndex + 1}页";
                        string imagePath = AvailablePath(
                            invoiceImageDirectory,
                            $"{imageBaseName}{pageSuffix}",
                            ".png");
                        File.Move(renderedPages[pageIndex], imagePath);
                        invoiceImageCount++;
                    }

                    string originalBaseName = string.Join(
                        "-",
                        invoiceNumber,
                        amount,
                        FileNamePart(invoice.MerchantName, "未填写商家名"),
                        FileNamePart(invoice.PrimaryProductDisplay, "未填写商品名"));
                    string originalExtension = Path.GetExtension(material.OriginalFileName);
                    File.Copy(
                        material.ManagedPath,
                        AvailablePath(invoiceOriginalDirectory, originalBaseName, originalExtension));
                }

                string orderProductName = FileNamePart(detail.ProductDisplay, "未填写商品名");
                ManagedMaterial[] supportingMaterials = detail.Materials
                    .Where(material => material.Role == ManagedFileRole.OrderScreenshot)
                    .ToArray();
                for (int materialIndex = 0; materialIndex < supportingMaterials.Length; materialIndex++)
                {
                    ManagedMaterial material = supportingMaterials[materialIndex];
                    string baseName = $"{orderProductName}-{FormatAmount(orderTotalMinorUnits)}-辅助材料-{materialIndex + 1}";
                    File.Copy(
                        material.ManagedPath,
                        AvailablePath(
                            supportingMaterialDirectory,
                            baseName,
                            Path.GetExtension(material.OriginalFileName)));
                    await ExportPrintableMaterialAsync(material, baseName, printDirectory, renderRoot, cancellationToken);
                    supportingMaterialCount++;
                }
            }

            string csvPath = AvailablePath(
                destinationRoot,
                $"发票导出-{exportedAt:yyyyMMdd-HHmmss}",
                ".csv");
            await File.WriteAllTextAsync(
                csvPath,
                BuildCsv(snapshots),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
                cancellationToken);

            return new ExportBatchResult(
                orderIds.Length,
                snapshots.Sum(snapshot => snapshot.Detail.Invoices.Count),
                invoiceImageCount,
                supportingMaterialCount,
                csvPath);
        }
        finally
        {
            if (Directory.Exists(renderRoot))
            {
                Directory.Delete(renderRoot, recursive: true);
            }
        }
    }

    private async Task ExportPrintableMaterialAsync(ManagedMaterial material, string baseName,
        string printDirectory, string renderRoot, CancellationToken cancellationToken)
    {
        if (material.MediaType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            var pages = await _pdfPageRenderer.RenderAsync(material.ManagedPath,
                Path.Combine(renderRoot, material.Id.ToString()), cancellationToken);
            if (pages.Count == 0) throw new InvalidDataException($"辅助材料“{material.OriginalFileName}”未生成图片。");
            for (int page = 0; page < pages.Count; page++)
                File.Move(pages[page], AvailablePath(printDirectory, $"{baseName}-第{page + 1}页", ".png"));
        }
        else if (material.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(material.ManagedPath, AvailablePath(printDirectory, baseName, Path.GetExtension(material.OriginalFileName)));
        }
    }

    private static string BuildCsv(IEnumerable<OrderExportSnapshot> snapshots)
    {
        StringBuilder csv = new("总金额,发票号\r\n");
        long selectedOrdersTotalMinorUnits = 0;
        foreach (OrderExportSnapshot snapshot in snapshots)
        {
            selectedOrdersTotalMinorUnits += snapshot.TotalMinorUnits;
            string invoiceNumbers = string.Join(
                ' ',
                snapshot.Detail.Invoices.Select(invoice => invoice.InvoiceNumber.Trim()));
            csv.Append(FormatAmount(snapshot.TotalMinorUnits))
                .Append(',')
                .Append(CsvField(invoiceNumbers))
                .Append("\r\n");
        }

        csv.Append(FormatAmount(selectedOrdersTotalMinorUnits))
            .Append(",合计\r\n");

        return csv.ToString();
    }

    private static string CsvField(string value) => value.IndexOfAny([',', '"', '\r', '\n']) < 0
        ? value
        : $"\"{value.Replace("\"", "\"\"")}\"";

    private static string FormatAmount(long minorUnits) => (minorUnits / 100m)
        .ToString("0.00", CultureInfo.InvariantCulture);

    private static string FileNamePart(string? value, string fallback)
    {
        string part = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
        {
            part = part.Replace(invalidCharacter, '_');
        }

        return part.TrimEnd(' ', '.');
    }

    private static string AvailablePath(string directory, string baseName, string extension)
    {
        string path = Path.Combine(directory, baseName + extension);
        for (int suffix = 2; File.Exists(path) || Directory.Exists(path); suffix++)
        {
            path = Path.Combine(directory, $"{baseName}-{suffix}{extension}");
        }

        return path;
    }

    private sealed record OrderExportSnapshot(OrderDetail Detail, long TotalMinorUnits);
}
