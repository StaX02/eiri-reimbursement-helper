using System.IO;
using Eiri.Reimbursement.Core.Documents;
using Eiri.Reimbursement.Infrastructure.Documents;

namespace Eiri.Reimbursement.Desktop;

internal static class DocumentProcessorBootstrap
{
    public static IDocumentProcessor? TryCreate(string baseDirectory)
    {
        string? configuredPython = Environment.GetEnvironmentVariable("EIRI_DOCUMENT_WORKER_PYTHON");
        string? configuredScript = Environment.GetEnvironmentVariable("EIRI_DOCUMENT_WORKER_SCRIPT");
        if (!string.IsNullOrWhiteSpace(configuredPython)
            && !string.IsNullOrWhiteSpace(configuredScript)
            && File.Exists(configuredPython)
            && File.Exists(configuredScript))
        {
            return new JsonLinesProcessDocumentProcessor(configuredPython, [configuredScript]);
        }

        string bundledWorker = Path.Combine(
            Path.GetFullPath(baseDirectory),
            "document-worker",
            "eiri-document-worker.exe");
        if (File.Exists(bundledWorker))
        {
            return new JsonLinesProcessDocumentProcessor(bundledWorker, []);
        }

        DirectoryInfo? directory = new(Path.GetFullPath(baseDirectory));
        while (directory is not null)
        {
            string workerRoot = Path.Combine(directory.FullName, "worker", "document-worker");
            string pythonPath = Path.Combine(workerRoot, ".venv", "Scripts", "python.exe");
            string scriptPath = Path.Combine(workerRoot, "src", "eiri_document_worker", "__main__.py");
            if (File.Exists(pythonPath) && File.Exists(scriptPath))
            {
                return new JsonLinesProcessDocumentProcessor(pythonPath, [scriptPath]);
            }

            directory = directory.Parent;
        }

        return null;
    }
}
