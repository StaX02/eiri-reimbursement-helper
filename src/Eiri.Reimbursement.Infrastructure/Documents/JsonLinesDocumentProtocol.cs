using System.Text.Json;
using Eiri.Reimbursement.Core.Documents;

namespace Eiri.Reimbursement.Infrastructure.Documents;

public static class JsonLinesDocumentProtocol
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static string SerializeRequest(DocumentJob job) =>
        JsonSerializer.Serialize(new AnalyzeRequest(CurrentVersion, job), SerializerOptions);

    public static DocumentAnalysis DeserializeResponse(string json)
    {
        AnalyzeResponse? response = JsonSerializer.Deserialize<AnalyzeResponse>(json, SerializerOptions);
        if (response is null)
        {
            throw new InvalidDataException("Document worker returned an empty response.");
        }

        if (response.ProtocolVersion != CurrentVersion)
        {
            throw new InvalidDataException(
                $"Unsupported document worker protocol version '{response.ProtocolVersion}'.");
        }

        return response.Analysis;
    }

    private sealed record AnalyzeRequest(int ProtocolVersion, DocumentJob Job);

    private sealed record AnalyzeResponse(int ProtocolVersion, DocumentAnalysis Analysis);
}
