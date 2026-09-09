using System.Net.Http.Headers;
using System.Text.Json;
using Eiri.Reimbursement.Core.DingTalk;

namespace Eiri.Reimbursement.Infrastructure.DingTalk;

public sealed class DingTalkMediaUploadClient(HttpClient httpClient, IDingTalkImageMetadataReader metadataReader) : IDingTalkImagePublisher
{
    private readonly SemaphoreSlim _slots = new(3);
    public async Task<DingTalkUploadedImage> UploadAsync(string accessToken, string imagePath, CancellationToken cancellationToken = default)
    {
        await _slots.WaitAsync(cancellationToken);
        try { return await UploadCoreAsync(accessToken, imagePath, cancellationToken); }
        finally { _slots.Release(); }
    }

    private async Task<DingTalkUploadedImage> UploadCoreAsync(string accessToken, string imagePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        using var stream = File.OpenRead(imagePath);
        if (stream.Length is <= 0 or > 20 * 1024 * 1024) throw new InvalidOperationException("图片必须非空且不超过 20 MB。");
        DingTalkImageMetadata metadata;
        try { metadata = metadataReader.Read(stream, imagePath); }
        catch (Exception ex) when (ex is not OperationCanceledException) { throw new InvalidOperationException("图片无法解析，请选择有效的 JPG、PNG、GIF 或 BMP 文件。"); }
        stream.Position = 0;
        using var form = new MultipartFormDataContent();
        using var file = new StreamContent(stream);
        file.Headers.ContentType = new MediaTypeHeaderValue(metadata.ContentType);
        file.Headers.ContentLength = stream.Length;
        form.Add(file, "media", "image." + metadata.Extension);
        // This legacy endpoint requires query-string credentials and type=image for C# uploads.
        using var request = new HttpRequestMessage(HttpMethod.Post,
            "https://oapi.dingtalk.com/media/upload?type=image&access_token=" + Uri.EscapeDataString(accessToken)) { Content = form };
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"上传失败（HTTP {(int)response.StatusCode}），请检查网络及钉钉连接后重试。");
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            int code = root.GetProperty("errcode").GetInt32();
            if (code != 0) throw new InvalidOperationException($"钉钉上传失败（错误码 {code}），请检查连接令牌、权限及图片格式后重试。");
            string mediaId = root.GetProperty("media_id").GetString() ?? "";
            return DingTalkUploadedImage.Create(mediaId, metadata);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException)
        { throw new InvalidOperationException("钉钉上传响应缺少有效的媒体标识，请重试。"); }
    }
}
