namespace Eiri.Reimbursement.Core.Documents;

public interface IDocumentProcessor
{
    Task<DocumentAnalysis> AnalyzeAsync(
        DocumentJob job,
        CancellationToken cancellationToken = default);
}
