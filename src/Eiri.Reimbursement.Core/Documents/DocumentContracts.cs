namespace Eiri.Reimbursement.Core.Documents;

public sealed record DocumentJob(
    Guid JobId,
    string FilePath,
    DocumentKind Kind,
    TimeSpan Timeout);

public enum DocumentKind
{
    InvoicePdf = 1,
    OrderScreenshot = 2,
}

public sealed record TextBounds(double X, double Y, double Width, double Height);

public sealed record TextBlock(
    string Text,
    int Page,
    TextBounds Bounds,
    double Confidence,
    string Source);

public sealed record FieldCandidate(
    string Field,
    string Value,
    double Confidence,
    string Source,
    int? Page = null,
    TextBounds? Bounds = null);

public sealed record DocumentAnalysis(
    string WorkerVersion,
    string ParserVersion,
    IReadOnlyList<TextBlock> TextBlocks,
    IReadOnlyList<FieldCandidate> Candidates,
    bool NeedsReview);
