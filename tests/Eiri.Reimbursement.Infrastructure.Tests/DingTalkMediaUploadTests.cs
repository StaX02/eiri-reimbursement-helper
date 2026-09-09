using System.Net;
using System.Text.Json;
using Eiri.Reimbursement.Core.DingTalk;
using Eiri.Reimbursement.Infrastructure.DingTalk;

namespace Eiri.Reimbursement.Infrastructure.Tests;

public sealed class DingTalkMediaUploadTests
{
    [Theory]
    [InlineData("@#123abc_9-XY", "abc_9-XY")]
    [InlineData("abc@12", "abc@12")]
    [InlineData("123__AbC", "AbC")]
    public void NormalizesOnlyLeadingNonLettersAndBuildsUrlWithOriginalDimensions(string raw, string clean)
    {
        var image = DingTalkUploadedImage.Create(raw, new(2048, 1536, ".PNG", "image/png"));
        Assert.Equal(raw, image.RawMediaId); Assert.Equal(clean, image.MediaId);
        Assert.Equal($"https://static.dingtalk.com/media/{Uri.EscapeDataString(clean)}_2048_1536.png", image.Url.AbsoluteUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("@#123_-")]
    public void RejectsMediaIdWithoutAnyLetter(string raw)
        => Assert.Throws<InvalidOperationException>(() => DingTalkUploadedImage.Create(raw, new(1, 1, "png", "image/png")));

    [Fact]
    public async Task UploadUsesMultipartMediaAndQueryTypeAndCachesNormalizedResponse()
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);
            using var http = new HttpClient(new Handler(async request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("https://oapi.dingtalk.com/media/upload?type=image&access_token=test%2Btoken", request.RequestUri!.AbsoluteUri);
                var form = Assert.IsType<MultipartFormDataContent>(request.Content);
                var part = Assert.Single(form);
                Assert.Equal("media", part.Headers.ContentDisposition!.Name!.Trim('"'));
                Assert.Equal("image.png", part.Headers.ContentDisposition.FileName!.Trim('"'));
                Assert.Equal("image/png", part.Headers.ContentType!.MediaType);
                Assert.Equal(new byte[] { 1, 2, 3, 4 }, await part.ReadAsByteArrayAsync());
                return Json("""{"errcode":0,"media_id":"@#12abc","type":"image"}""");
            }));
            var image = await new DingTalkMediaUploadClient(http, new Metadata()).UploadAsync("test+token", path);
            Assert.Equal("abc", image.MediaId);
            Assert.Equal("https://static.dingtalk.com/media/abc_2000_1000.png", image.Url.AbsoluteUri);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("{\"errcode\":40014,\"errmsg\":\"secret-token\"}")]
    [InlineData("{\"errcode\":0}")]
    [InlineData("not json")]
    public async Task FailedResponsesDoNotLeakBodyOrToken(string body)
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, [1]);
            using var http = new HttpClient(new Handler(_ => Task.FromResult(Json(body))));
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new DingTalkMediaUploadClient(http, new Metadata()).UploadAsync("secret-token", path));
            Assert.DoesNotContain("secret-token", error.Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task EmptyOrOversizedImageDoesNotCallApi()
    {
        string path = Path.GetTempFileName();
        try
        {
            using var http = new HttpClient(new Handler(_ => throw new Exception("Must not send")));
            var client = new DingTalkMediaUploadClient(http, new Metadata());
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.UploadAsync("token", path));
            using (var file = File.OpenWrite(path)) file.SetLength(20 * 1024 * 1024 + 1);
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.UploadAsync("token", path));
        }
        finally { File.Delete(path); }
    }
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    private sealed class Metadata : IDingTalkImageMetadataReader
    {
        public DingTalkImageMetadata Read(Stream stream, string fileName) { stream.ReadByte(); return new(2000, 1000, "png", "image/png"); }
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
