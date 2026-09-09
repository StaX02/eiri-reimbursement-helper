using Eiri.Reimbursement.Core.DingTalk;
using Eiri.Reimbursement.Desktop.ViewModels;

namespace Eiri.Reimbursement.Desktop.Tests;

public sealed class DingTalkConnectionTests
{
    [Fact]
    public async Task ConcurrentClicksDoNotOpenTwoImportsOrIssueTwoRequests()
    {
        var store = new Store(); var client = new Client(); var vm = new DingTalkConnectionViewModel(client, store);
        var credentials = new TaskCompletionSource<DingTalkCredentials?>();
        var pending = vm.ConnectAsync(_ => credentials.Task);
        Assert.True(vm.IsBusy);
        Assert.False(await vm.ConnectAsync(_ => throw new InvalidOperationException("Second import")));
        credentials.SetResult(new("key", "secret")); Assert.True(await pending);
        Assert.Equal(1, client.Calls);
    }
    [Fact]
    public async Task ImportsOnlyWhenMissingAndPersistsExpiryThenExpiresAtBoundary()
    {
        DateTimeOffset now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        var store = new Store(); var client = new Client();
        var vm = new DingTalkConnectionViewModel(client, store, () => now);
        await vm.InitializeAsync(); Assert.Equal(DingTalkConnectionState.Disconnected, vm.State);
        int imports = 0;
        Task<DingTalkCredentials?> Import(CancellationToken _) { imports++; return Task.FromResult<DingTalkCredentials?>(DingTalkCredentials.Parse("""{"appKey":"key","appSecret":"secret"}""")); }
        Assert.True(await vm.ConnectAsync(Import));
        Assert.Equal(now.AddSeconds(7200), store.Record!.ExpiresAt);
        Assert.Equal("key", client.Key); Assert.Equal("secret", client.Secret);
        Assert.Equal(DingTalkConnectionState.Connected, vm.State);
        Assert.True(await vm.ConnectAsync(Import)); Assert.Equal(1, imports); Assert.Equal(2, client.Calls);
        now = now.AddSeconds(7200); vm.CheckExpiration(); Assert.Equal(DingTalkConnectionState.Expired, vm.State);
        var reopened = new DingTalkConnectionViewModel(client, store, () => now);
        await reopened.InitializeAsync(); Assert.Equal(DingTalkConnectionState.Expired, reopened.State);
        store.Record = null; await vm.InitializeAsync(); Assert.Equal(DingTalkConnectionState.Disconnected, vm.State);
    }

    [Fact]
    public async Task FailedRefreshPreservesStoredConnectionAndReimportReplacesCredentials()
    {
        var saved = new DingTalkConnection("old", "old-secret", "old-token", DateTimeOffset.UtcNow.AddHours(1));
        var store = new Store { Record = saved }; var client = new Client { Fail = true };
        var vm = new DingTalkConnectionViewModel(client, store); await vm.InitializeAsync();
        Assert.False(await vm.ConnectAsync(_ => throw new InvalidOperationException("Unexpected picker")));
        Assert.Equal(DingTalkConnectionState.Error, vm.State); Assert.True(vm.HasError); Assert.Equal(saved, store.Record);
        vm.CheckExpiration(); Assert.Equal(DingTalkConnectionState.Error, vm.State);
        Assert.False(await vm.ConnectAsync(_ => Task.FromResult<DingTalkCredentials?>(null), true));
        Assert.Equal(saved, store.Record);
        client.Fail = false;
        Assert.True(await vm.ConnectAsync(_ => Task.FromResult<DingTalkCredentials?>(new("new", "new-secret")), true));
        Assert.Equal("new", store.Record!.ClientId); Assert.Equal(DingTalkConnectionState.Connected, vm.State);
    }

    [Fact]
    public async Task CancelAndSaveFailureNeverReplaceConnection()
    {
        var store = new Store(); var client = new Client(); var vm = new DingTalkConnectionViewModel(client, store);
        Assert.False(await vm.ConnectAsync(_ => Task.FromResult<DingTalkCredentials?>(null)));
        Assert.Equal(0, client.Calls); Assert.False(vm.HasError);
        store.FailSave = true;
        Assert.False(await vm.ConnectAsync(_ => Task.FromResult<DingTalkCredentials?>(new("key", "secret"))));
        Assert.Null(store.Record); Assert.Equal(DingTalkConnectionState.Error, vm.State);
        Assert.Empty(vm.AccessToken);
    }

    [Theory]
    [InlineData("{}")] [InlineData("[]")] [InlineData("{\"appKey\":1,\"appSecret\":\"secret\"}")]
    [InlineData("{\"appKey\":\"key\",\"appSecret\":\" \"}")] [InlineData("{\"appSecret\":\"sensitive-secret")]
    public void InvalidCredentialJsonHasSafeError(string json)
    {
        var error = Assert.Throws<InvalidOperationException>(() => DingTalkCredentials.Parse(json));
        Assert.DoesNotContain("sensitive-secret", error.Message);
    }

    [Fact]
    public async Task LegacyTokenWithoutExpiryRequiresReconnect()
    {
        var store = new Store { Record = new("key", "secret", "old-token") };
        var vm = new DingTalkConnectionViewModel(new Client(), store); await vm.InitializeAsync();
        Assert.Equal(DingTalkConnectionState.Expired, vm.State);
    }

    private sealed class Store : IDingTalkConnectionStore
    {
        public DingTalkConnection? Record { get; set; }
        public bool FailSave { get; set; }
        public Task<DingTalkConnection?> GetDingTalkConnectionAsync(CancellationToken cancellationToken = default) => Task.FromResult(Record);
        public Task SaveDingTalkConnectionAsync(DingTalkConnection connection, CancellationToken cancellationToken = default)
        {
            if (FailSave) throw new System.IO.IOException();
            Record = connection; return Task.CompletedTask;
        }
        public Task ClearDingTalkConnectionAsync(CancellationToken cancellationToken = default) { Record = null; return Task.CompletedTask; }
    }
    private sealed class Client : IDingTalkAccessTokenClient
    {
        public bool Fail { get; set; }
        public int Calls { get; private set; }
        public string? Key { get; private set; }
        public string? Secret { get; private set; }
        public Task<DingTalkAccessToken> GetAccessTokenAsync(string clientId, string clientSecret, CancellationToken cancellationToken = default)
        {
            Calls++; Key = clientId; Secret = clientSecret;
            if (Fail) throw new InvalidOperationException("连接失败");
            return Task.FromResult(new DingTalkAccessToken("token", 7200));
        }
    }
}
