namespace Eiri.Reimbursement.Core.Documents;

public interface IPdfPageRenderer
{
    Task<IReadOnlyList<string>> RenderAsync(
        string pdfPath,
        string destinationDirectory,
        CancellationToken cancellationToken = default);
}
