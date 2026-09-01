using System.Diagnostics;
using Eiri.Reimbursement.Core.Documents;

namespace Eiri.Reimbursement.Infrastructure.Documents;

public sealed class JsonLinesProcessDocumentProcessor(
    string executablePath,
    IReadOnlyList<string> arguments) : IDocumentProcessor
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
        timeout.CancelAfter(job.Timeout);

        try
        {
            string request = JsonLinesDocumentProtocol.SerializeRequest(job);
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

            return JsonLinesDocumentProtocol.DeserializeResponse(response);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryTerminate(process);
            throw new TimeoutException($"Document worker exceeded the {job.Timeout} timeout.");
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
