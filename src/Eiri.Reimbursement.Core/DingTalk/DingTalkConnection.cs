namespace Eiri.Reimbursement.Core.DingTalk;

public sealed record DingTalkConnection(string ClientId, string ClientSecret, string AccessToken, DateTimeOffset? ExpiresAt = null)
{
    public override string ToString() => "DingTalkConnection { [redacted] }";
}

public interface IDingTalkAccessTokenClient
{
    Task<DingTalkAccessToken> GetAccessTokenAsync(string clientId, string clientSecret, CancellationToken cancellationToken = default);
}

public sealed record DingTalkAccessToken(string AccessToken, long ExpireIn)
{
    public override string ToString() => "DingTalkAccessToken { [redacted] }";
}

public sealed record DingTalkCredentials(string AppKey, string AppSecret)
{
    public override string ToString() => "DingTalkCredentials { [redacted] }";

    public static DingTalkCredentials Parse(string json)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind == System.Text.Json.JsonValueKind.Object
                && root.TryGetProperty("appKey", out var key) && key.ValueKind == System.Text.Json.JsonValueKind.String
                && root.TryGetProperty("appSecret", out var secret) && secret.ValueKind == System.Text.Json.JsonValueKind.String
                && !string.IsNullOrWhiteSpace(key.GetString()) && !string.IsNullOrWhiteSpace(secret.GetString()))
                return new(key.GetString()!.Trim(), secret.GetString()!);
        }
        catch (System.Text.Json.JsonException) { }
        throw new InvalidOperationException("凭据文件必须是包含非空 appKey 和 appSecret 字符串字段的 JSON 对象。");
    }
}

public interface IDingTalkConnectionStore
{
    Task<DingTalkConnection?> GetDingTalkConnectionAsync(CancellationToken cancellationToken = default);
    Task SaveDingTalkConnectionAsync(DingTalkConnection connection, CancellationToken cancellationToken = default);
    Task ClearDingTalkConnectionAsync(CancellationToken cancellationToken = default);
}
