using CommunityToolkit.Mvvm.ComponentModel;
using Eiri.Reimbursement.Core.DingTalk;

namespace Eiri.Reimbursement.Desktop.ViewModels;

public enum DingTalkConnectionState { Disconnected, Connected, Error, Expired, Connecting }

public sealed partial class DingTalkConnectionViewModel(
    IDingTalkAccessTokenClient? client, IDingTalkConnectionStore? store, Func<DateTimeOffset>? now = null) : ObservableObject
{
    private readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.UtcNow);
    private DingTalkConnection? _connection;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(StatusText))] private DingTalkConnectionState _state;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasError))] private string _errorMessage = "";
    [ObservableProperty] private bool _isBusy;
    public bool HasError => ErrorMessage.Length > 0;
    public string StatusText => State switch
    {
        DingTalkConnectionState.Connected => "钉钉：连接成功",
        DingTalkConnectionState.Error => "钉钉：连接错误",
        DingTalkConnectionState.Expired => "钉钉：连接过期",
        DingTalkConnectionState.Connecting => "钉钉：正在连接",
        _ => "钉钉：未连接"
    };
    public string AccessToken => HasError ? "" : _connection?.AccessToken ?? "";
    public string ExpirationText => _connection?.ExpiresAt is { } expires
        ? "过期时间：" + expires.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "过期时间未知，请重新连接。";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy || store is null) return;
        IsBusy = true;
        try { _connection = await store.GetDingTalkConnectionAsync(cancellationToken); ErrorMessage = ""; UpdateState(); }
        catch (Exception) { Fail("无法读取钉钉连接记录，请检查资料库访问权限。"); }
        finally { IsBusy = false; NotifyConnection(); }
    }

    public void CheckExpiration()
    {
        if (!IsBusy && State is not DingTalkConnectionState.Error) UpdateState();
    }
    private void UpdateState() => State = _connection is null || string.IsNullOrWhiteSpace(_connection.AccessToken)
        || string.IsNullOrWhiteSpace(_connection.ClientId) || string.IsNullOrWhiteSpace(_connection.ClientSecret)
        ? DingTalkConnectionState.Disconnected
        : _connection.ExpiresAt is not { } expires || expires <= _now() ? DingTalkConnectionState.Expired : DingTalkConnectionState.Connected;

    public async Task<bool> ConnectAsync(Func<CancellationToken, Task<DingTalkCredentials?>> import,
        bool forceImport = false, CancellationToken cancellationToken = default)
    {
        if (IsBusy || client is null || store is null) return false;
        IsBusy = true;
        var previousState = State;
        try
        {
            var saved = await store.GetDingTalkConnectionAsync(cancellationToken);
            DingTalkCredentials? credentials;
            if (forceImport || saved is null || string.IsNullOrWhiteSpace(saved.ClientId) || string.IsNullOrWhiteSpace(saved.ClientSecret))
                credentials = await import(cancellationToken);
            else credentials = new(saved.ClientId, saved.ClientSecret);
            if (credentials is null) { ErrorMessage = ""; State = previousState; return false; }
            ErrorMessage = ""; State = DingTalkConnectionState.Connecting;
            var token = await client.GetAccessTokenAsync(credentials.AppKey, credentials.AppSecret, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(token.AccessToken) || token.ExpireIn is <= 0 or > int.MaxValue)
                throw new InvalidOperationException("钉钉返回的令牌或过期秒数无效。");
            var connection = new DingTalkConnection(credentials.AppKey, credentials.AppSecret, token.AccessToken, _now().AddSeconds(token.ExpireIn));
            await store.SaveDingTalkConnectionAsync(connection, cancellationToken);
            _connection = connection; UpdateState(); return true;
        }
        catch (OperationCanceledException) { if (!cancellationToken.IsCancellationRequested) Fail("连接超时，请检查网络后重试。"); else State = previousState; }
        catch (InvalidOperationException ex) { Fail(ex.Message); }
        catch (Exception) { Fail("连接或导入凭据失败，请检查 JSON 文件、网络及资料库访问权限。"); }
        finally { IsBusy = false; NotifyConnection(); }
        return false;
    }
    private void Fail(string message) { ErrorMessage = message; State = DingTalkConnectionState.Error; }
    private void NotifyConnection() { OnPropertyChanged(nameof(AccessToken)); OnPropertyChanged(nameof(ExpirationText)); }
}
