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

    [Fact]
    public async Task RenderedPagesReturnedByWorkerAreAvailableThroughPdfPageRenderer()
    {
        string testRoot = Path.Combine(Path.GetTempPath(), "eiri-render-tests", Guid.NewGuid().ToString("N"));
        try
        {
            string workerScript = Path.Combine(AppContext.BaseDirectory, "Fixtures", "fake_document_worker.py");
            IPdfPageRenderer renderer = new JsonLinesProcessDocumentProcessor("python", [workerScript]);

            IReadOnlyList<string> pages = await renderer.RenderAsync(
                Path.Combine(testRoot, "invoice.pdf"),
                Path.Combine(testRoot, "pages"));

            Assert.Equal(["page-1.png", "page-2.png"], pages.Select(Path.GetFileName));
            Assert.All(pages, path => Assert.True(File.Exists(path)));
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }
}
