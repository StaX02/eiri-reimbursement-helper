using System.Net;
using System.Text;
using System.Text.Json;
using Eiri.Reimbursement.Infrastructure.DingTalk;
using Eiri.Reimbursement.Infrastructure.Sqlite;
using Eiri.Reimbursement.Core.DingTalk;
using Eiri.Reimbursement.Core.Orders;
using Microsoft.Data.Sqlite;

namespace Eiri.Reimbursement.Infrastructure.Tests;

public sealed class DingTalkTests
{
    [Fact]
    public async Task ClientCredentialsReturnAccessToken()
    {
        using HttpClient http = new(new Handler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://api.dingtalk.com/v1.0/oauth2/accessToken", request.RequestUri!.ToString());
            using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("test-client", body.RootElement.GetProperty("appKey").GetString());
            Assert.Equal("test-secret", body.RootElement.GetProperty("appSecret").GetString());
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"accessToken\":\"test-token\",\"expireIn\":7200}", Encoding.UTF8, "application/json") };
        }));
        Assert.Equal(new DingTalkAccessToken("test-token", 7200), await new DingTalkAccessTokenClient(http).GetAccessTokenAsync("test-client", "test-secret"));
    }

    [Theory]
    [InlineData(400, "{\"message\":\"test-secret\"}")]
    [InlineData(200, "{}")]
    [InlineData(200, "{\"accessToken\":\" \"}")]
    [InlineData(200, "{\"accessToken\":123}")]
    [InlineData(200, "not-json")]
    [InlineData(200, "{\"accessToken\":\"valid\",\"expireIn\":0}")]
    [InlineData(200, "{\"accessToken\":\"valid\",\"expireIn\":-1}")]
    [InlineData(200, "{\"accessToken\":\"valid\",\"expireIn\":\"7200\"}")]
    [InlineData(200, "{\"accessToken\":\"valid\"}")]
    public async Task FailedOrMalformedResponsesDoNotReturnTokensOrExposeResponseBody(int status, string body)
    {
        using HttpClient http = new(new Handler(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) })));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new DingTalkAccessTokenClient(http).GetAccessTokenAsync("test-client", "test-secret"));
        Assert.DoesNotContain("test-secret", error.Message);
    }

    [Fact]
    public async Task EmptyCredentialsNeverMakeARequest()
    {
        using HttpClient http = new(new Handler(_ => throw new Xunit.Sdk.XunitException("Unexpected HTTP request")));
        await Assert.ThrowsAsync<ArgumentException>(() => new DingTalkAccessTokenClient(http).GetAccessTokenAsync(" ", "test-secret"));
    }

    [Fact]
    public async Task ConnectionCanBeReplacedReopenedAndClearedWithoutDeletingOrders()
    {
        string root = Path.Combine(Path.GetTempPath(), "eiri-dingtalk-tests", Guid.NewGuid().ToString("N"));
        try
        {
            SqliteReimbursementWorkspace workspace = new(root);
            await workspace.InitializeAsync();
            await workspace.CreateOrderAsync(new(OrderPlatform.Other));
            Assert.Null(await workspace.GetDingTalkConnectionAsync());
            await workspace.SaveDingTalkConnectionAsync(new("test-one", "test-secret-one", "test-token-one"));
            DingTalkConnection replacement = new("test-two", "test-secret-two", "test-token-two", DateTimeOffset.UtcNow.AddHours(2));
            await workspace.SaveDingTalkConnectionAsync(replacement);
            SqliteReimbursementWorkspace reopened = new(root);
            await reopened.InitializeAsync();
            Assert.Equal(replacement, await reopened.GetDingTalkConnectionAsync());
            await reopened.ClearDingTalkConnectionAsync();
            await reopened.ClearDingTalkConnectionAsync();
            Assert.Null(await workspace.GetDingTalkConnectionAsync());
            Assert.Single(await workspace.SearchOrdersAsync(new()));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ExistingVersionFiveLibraryUpgradesWithoutLosingOrders()
    {
        string root = Path.Combine(Path.GetTempPath(), "eiri-dingtalk-tests", Guid.NewGuid().ToString("N"));
        try
        {
            SqliteReimbursementWorkspace workspace = new(root);
            await workspace.InitializeAsync();
            await workspace.CreateOrderAsync(new(OrderPlatform.Other));
            // Recreate the pre-connection schema to exercise an existing user's upgrade.
            await using (SqliteConnection db = new(new SqliteConnectionStringBuilder { DataSource = Path.Combine(root, "library.db"), Pooling = false }.ToString()))
            {
                await db.OpenAsync();
                await using SqliteCommand sql = db.CreateCommand();
                sql.CommandText = "DROP TABLE dingtalk_connection; PRAGMA user_version = 5;";
                await sql.ExecuteNonQueryAsync();
            }
            await workspace.InitializeAsync();
            DingTalkConnection record = new("test-client", "test-secret", "test-token");
            await workspace.SaveDingTalkConnectionAsync(record);
            Assert.Equal(record, await workspace.GetDingTalkConnectionAsync());
            Assert.Single(await workspace.SearchOrdersAsync(new()));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
