using System.IO;
using Eiri.Reimbursement.Desktop;

namespace Eiri.Reimbursement.Desktop.Tests;

public sealed class DocumentProcessorBootstrapTests
{
    [Fact]
    public void BundledWorkerCanBeUsedWithoutPythonEnvironment()
    {
        string testRoot = Path.Combine(Path.GetTempPath(), "eiri-worker-bootstrap-tests", Guid.NewGuid().ToString("N"));
        try
        {
            string workerDirectory = Path.Combine(testRoot, "document-worker");
            Directory.CreateDirectory(workerDirectory);
            File.WriteAllBytes(Path.Combine(workerDirectory, "eiri-document-worker.exe"), []);

            Assert.NotNull(DocumentProcessorBootstrap.TryCreate(testRoot));
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
