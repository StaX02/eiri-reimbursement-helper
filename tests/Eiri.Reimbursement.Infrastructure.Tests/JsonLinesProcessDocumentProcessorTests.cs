using Eiri.Reimbursement.Core.Documents;
using Eiri.Reimbursement.Infrastructure.Documents;

namespace Eiri.Reimbursement.Infrastructure.Tests;

public sealed class JsonLinesProcessDocumentProcessorTests
{
    [Fact]
    public async Task AnalysisReturnedByWorkerIsAvailableThroughDocumentProcessor()
    {
        string workerScript = Path.Combine(AppContext.BaseDirectory, "Fixtures", "fake_document_worker.py");
        IDocumentProcessor processor = new JsonLinesProcessDocumentProcessor("python", [workerScript]);

        DocumentAnalysis analysis = await processor.AnalyzeAsync(new DocumentJob(
            Guid.NewGuid(),
            "invoice.pdf",
            DocumentKind.InvoicePdf,
            TimeSpan.FromSeconds(10)));

        Assert.Equal("fake-1.0", analysis.WorkerVersion);
        Assert.Equal("EIRI-INV-001", Assert.Single(analysis.TextBlocks).Text);
    }
}
