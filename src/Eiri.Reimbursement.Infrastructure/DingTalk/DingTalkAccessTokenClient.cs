using System.Net.Http.Json;
using System.Text.Json;
using Eiri.Reimbursement.Core.DingTalk;

namespace Eiri.Reimbursement.Infrastructure.DingTalk;

public sealed class DingTalkAccessTokenClient(HttpClient httpClient) : IDingTalkAccessTokenClient
{
    public async Task<DingTalkAccessToken> GetAccessTokenAsync(string clientId, string clientSecret, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientSecret);
        // Client ID/Secret map to appKey/appSecret in DingTalk's internal-app API.
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "https://api.dingtalk.com/v1.0/oauth2/accessToken",
            new { appKey = clientId, appSecret = clientSecret }, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"钉钉连接失败（HTTP {(int)response.StatusCode}），请检查 Client ID、Client Secret 及应用权限后重试。");

        try
        {
            using JsonDocument body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (body.RootElement.ValueKind == JsonValueKind.Object
                && body.RootElement.TryGetProperty("accessToken", out JsonElement token)
                && token.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(token.GetString())
                && body.RootElement.TryGetProperty("expireIn", out var expiry)
                && expiry.ValueKind == JsonValueKind.Number && expiry.TryGetInt64(out long seconds) && seconds is > 0 and <= int.MaxValue)
                return new(token.GetString()!, seconds);
        }
        catch (JsonException) { }
        throw new InvalidOperationException("钉钉返回的 AccessToken 或过期秒数 expireIn 无效，请重试。");
    }
}
