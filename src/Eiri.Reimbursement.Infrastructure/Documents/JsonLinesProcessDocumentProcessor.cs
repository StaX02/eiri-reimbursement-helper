using System.Diagnostics;
using Eiri.Reimbursement.Core.Documents;

namespace Eiri.Reimbursement.Infrastructure.Documents;

public sealed class JsonLinesProcessDocumentProcessor(
    string executablePath,
    IReadOnlyList<string> arguments) : IDocumentProcessor, IPdfPageRenderer
{
    private readonly string _executablePath = executablePath;
    private readonly IReadOnlyList<string> _arguments = arguments;

    public async Task<DocumentAnalysis> AnalyzeAsync(
        DocumentJob job,
        CancellationToken cancellationToken = default)
    {
        if (job.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(job), "Document job timeout must be positive.");
        }

        string response = await ExecuteAsync(
            JsonLinesDocumentProtocol.SerializeRequest(job),
            job.Timeout,
            cancellationToken);
        return JsonLinesDocumentProtocol.DeserializeResponse(response);
    }

    public async Task<IReadOnlyList<string>> RenderAsync(
        string pdfPath,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        string destinationRoot = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(destinationRoot);
        string response = await ExecuteAsync(
            JsonLinesDocumentProtocol.SerializeRenderRequest(
                Path.GetFullPath(pdfPath),
                destinationRoot),
            TimeSpan.FromMinutes(2),
            cancellationToken);
        IReadOnlyList<string> renderedFiles = JsonLinesDocumentProtocol.DeserializeRenderResponse(response);
        string rootWithSeparator = destinationRoot.EndsWith(Path.DirectorySeparatorChar)
            ? destinationRoot
            : destinationRoot + Path.DirectorySeparatorChar;
        foreach (string renderedFile in renderedFiles)
        {
            string fullPath = Path.GetFullPath(renderedFile);
            if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
                || !File.Exists(fullPath))
            {
                throw new InvalidDataException("Document worker returned an invalid rendered page path.");
            }
        }

        return renderedFiles.Select(Path.GetFullPath).ToArray();
    }

    private async Task<string> ExecuteAsync(
        string request,
        TimeSpan timeoutDuration,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = _executablePath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (string argument in _arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Document worker process could not be started.");
        }

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutDuration);

        try
        {
            await process.StandardInput.WriteLineAsync(request.AsMemory(), timeout.Token);
            process.StandardInput.Close();

            Task<string?> responseTask = process.StandardOutput.ReadLineAsync(timeout.Token).AsTask();
            Task<string> errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            string? response = await responseTask;
            string error = await errorTask;

            if (process.ExitCode != 0)
            {
                throw new InvalidDataException(
                    $"Document worker exited with code {process.ExitCode}: {error.Trim()}");
            }

            if (string.IsNullOrWhiteSpace(response))
            {
                throw new InvalidDataException("Document worker returned no response.");
            }

            return response;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryTerminate(process);
            throw new TimeoutException($"Document worker exceeded the {timeoutDuration} timeout.");
        }
        finally
        {
            TryTerminate(process);
        }
    }

    private static void TryTerminate(Process process)
    {
        if (process.HasExited)
        {
            return;
        }

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process exited between HasExited and Kill.
        }
    }
}
