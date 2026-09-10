using System.Text.Json;
using Eiri.Reimbursement.Core.Reimbursements;

namespace Eiri.Reimbursement.Core.DingTalk;

public sealed record DingTalkComponentValue(string Id, string Name, string Value, string ComponentType);
public sealed record DingTalkForecastRequest(string ProcessCode, long DeptId, string UserId, IReadOnlyList<DingTalkComponentValue> FormComponentValues);

public interface IDingTalkForecastClient
{
    Task<JsonElement> ForecastAsync(string accessToken, DingTalkForecastRequest request, CancellationToken cancellationToken = default);
}

public sealed record DingTalkInvoiceImages(IReadOnlyList<string> ImagePaths, IReadOnlyList<string> InvoiceNumbers);
public interface IDingTalkInvoiceImages
{
    Task<DingTalkInvoiceImages> PrepareAsync(ReimbursementForm form, string destinationDirectory, CancellationToken cancellationToken = default);
}

public sealed record DingTalkImageMetadata(int Width, int Height, string Extension, string ContentType);
public interface IDingTalkImageMetadataReader
{
    DingTalkImageMetadata Read(Stream stream, string fileName);
}

public sealed record DingTalkUploadedImage(string RawMediaId, string MediaId, int Width, int Height, string Extension, Uri Url)
{
    public static DingTalkUploadedImage Create(string rawMediaId, DingTalkImageMetadata metadata)
    {
        int start = 0;
        while (start < rawMediaId.Length && !char.IsAsciiLetter(rawMediaId[start])) start++;
        string mediaId = rawMediaId[start..];
        string extension = metadata.Extension.TrimStart('.').ToLowerInvariant();
        if (mediaId.Length == 0 || metadata.Width <= 0 || metadata.Height <= 0 || extension is not ("png" or "jpg" or "jpeg" or "gif" or "bmp"))
            throw new InvalidOperationException("上传返回的媒体标识或图片信息无效。");
        var url = new Uri($"https://static.dingtalk.com/media/{Uri.EscapeDataString(mediaId)}_{metadata.Width}_{metadata.Height}.{extension}");
        return new(rawMediaId, mediaId, metadata.Width, metadata.Height, extension, url);
    }
}

public interface IDingTalkImagePublisher
{
    Task<DingTalkUploadedImage> UploadAsync(string accessToken, string imagePath, CancellationToken cancellationToken = default);
}
