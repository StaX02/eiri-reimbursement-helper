using System.Globalization;
using System.Text;
using Eiri.Reimbursement.Core;
using Eiri.Reimbursement.Core.Documents;
using Eiri.Reimbursement.Core.Export;
using Eiri.Reimbursement.Core.Invoices;
using Eiri.Reimbursement.Core.Materials;
using Eiri.Reimbursement.Core.Orders;

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
        OrderId[] orderIds = command.OrderIds.Distinct().ToArray();
        if (orderIds.Length == 0)
        {
            throw new ArgumentException("At least one order is required for export.", nameof(command));
        }

        string destinationRoot = Path.GetFullPath(command.DestinationDirectory);
        Directory.CreateDirectory(destinationRoot);
        string invoiceImageDirectory = Path.Combine(destinationRoot, "发票图片");
        string invoiceOriginalDirectory = Path.Combine(destinationRoot, "发票原件");
        string supportingMaterialDirectory = Path.Combine(destinationRoot, "报销辅助材料");
        Directory.CreateDirectory(invoiceImageDirectory);
        Directory.CreateDirectory(invoiceOriginalDirectory);
        Directory.CreateDirectory(supportingMaterialDirectory);

        List<OrderExportSnapshot> snapshots = [];
        int invoiceImageCount = 0;
        int supportingMaterialCount = 0;
        string renderRoot = Path.Combine(destinationRoot, $".eiri-render-{Guid.NewGuid():N}");
        try
        {
            foreach (OrderId orderId in orderIds)
            {
                OrderDetail detail = await _workspace.GetOrderAsync(orderId, cancellationToken)
                    ?? throw new KeyNotFoundException($"Order '{orderId}' was not found.");
                long orderTotalMinorUnits = detail.Invoices.Sum(invoice => invoice.TotalMinorUnits);
                snapshots.Add(new OrderExportSnapshot(detail, orderTotalMinorUnits));

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
                    supportingMaterialCount++;
                }
            }

            string csvPath = AvailablePath(
                destinationRoot,
                $"发票导出-{_clock():yyyyMMdd-HHmmss}",
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

    private static string BuildCsv(IEnumerable<OrderExportSnapshot> snapshots)
    {
        StringBuilder csv = new("总金额,发票号\r\n");
        foreach (OrderExportSnapshot snapshot in snapshots)
        {
            string invoiceNumbers = string.Join(
                ' ',
                snapshot.Detail.Invoices.Select(invoice => invoice.InvoiceNumber.Trim()));
            csv.Append(FormatAmount(snapshot.TotalMinorUnits))
                .Append(',')
                .Append(CsvField(invoiceNumbers))
                .Append("\r\n");
        }

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
        for (int suffix = 2; File.Exists(path); suffix++)
        {
            path = Path.Combine(directory, $"{baseName}-{suffix}{extension}");
        }

        return path;
    }

    private sealed record OrderExportSnapshot(OrderDetail Detail, long TotalMinorUnits);
}
