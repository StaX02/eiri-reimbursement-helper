namespace Eiri.Reimbursement.Core.Documents;

public interface IPdfPageRenderer
{
    async Task<IReadOnlyList<string>> RenderFirstPageAsync(string pdfPath, string destinationDirectory, CancellationToken cancellationToken = default)
        => (await RenderAsync(pdfPath, destinationDirectory, cancellationToken)).Take(1).ToArray();

    Task<IReadOnlyList<string>> RenderAsync(
        string pdfPath,
        string destinationDirectory,
        CancellationToken cancellationToken = default);
}
