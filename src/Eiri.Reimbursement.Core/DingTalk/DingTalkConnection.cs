namespace Eiri.Reimbursement.Core.DingTalk;

public sealed record DingTalkConnection(string ClientId, string ClientSecret, string AccessToken)
{
    public override string ToString() => "DingTalkConnection { [redacted] }";
}

public interface IDingTalkAccessTokenClient
{
    Task<string> GetAccessTokenAsync(string clientId, string clientSecret, CancellationToken cancellationToken = default);
}

public interface IDingTalkConnectionStore
{
    Task<DingTalkConnection?> GetDingTalkConnectionAsync(CancellationToken cancellationToken = default);
    Task SaveDingTalkConnectionAsync(DingTalkConnection connection, CancellationToken cancellationToken = default);
    Task ClearDingTalkConnectionAsync(CancellationToken cancellationToken = default);
}
