using System.Windows;
using Eiri.Reimbursement.Core.DingTalk;

namespace Eiri.Reimbursement.Desktop;

public partial class DingTalkConnectionWindow : Window
{
    private readonly IDingTalkAccessTokenClient _client;
    private readonly IDingTalkConnectionStore _store;
    private readonly CancellationTokenSource _lifetime = new();

    public DingTalkConnectionWindow(IDingTalkAccessTokenClient client, IDingTalkConnectionStore store, DingTalkConnection? saved = null)
    {
        InitializeComponent();
        _client = client;
        _store = store;
        ClientIdInput.Text = saved?.ClientId ?? "";
        ClientSecretInput.Password = saved?.ClientSecret ?? "";
        Loaded += (_, _) => ClientIdInput.Focus();
        Closed += (_, _) =>
        {
            _lifetime.Cancel();
            ClientSecretInput.Clear();
            AccessTokenOutput.Clear();
        };
    }

    private async void Connect_OnClick(object sender, RoutedEventArgs e)
    {
        if (!ConnectButton.IsEnabled) return;
        string clientId = ClientIdInput.Text.Trim();
        string secret = ClientSecretInput.Password;
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(secret))
        {
            StatusText.Text = "请填写 Client ID 和 Client Secret。";
            if (string.IsNullOrWhiteSpace(clientId)) ClientIdInput.Focus();
            else ClientSecretInput.Focus();
            return;
        }

        ConnectButton.IsEnabled = ClientIdInput.IsEnabled = ClientSecretInput.IsEnabled = false;
        AccessTokenOutput.Clear();
        StatusText.Text = "正在连接钉钉…";
        try
        {
            string token = await _client.GetAccessTokenAsync(clientId, secret, _lifetime.Token);
            _lifetime.Token.ThrowIfCancellationRequested();
            await _store.SaveDingTalkConnectionAsync(new(clientId, secret, token), _lifetime.Token);
            if (_lifetime.IsCancellationRequested) return;
            AccessTokenOutput.Text = token;
            StatusText.Text = "连接成功，连接记录已保存。";
        }
        catch (OperationCanceledException)
        {
            if (!_lifetime.IsCancellationRequested) StatusText.Text = "连接超时，请检查网络后重试。";
        }
        catch (InvalidOperationException exception) { StatusText.Text = exception.Message; }
        catch (Exception) { StatusText.Text = "连接或保存失败，请检查网络及资料库访问权限后重试。"; }
        finally { ConnectButton.IsEnabled = ClientIdInput.IsEnabled = ClientSecretInput.IsEnabled = true; }
    }
}
