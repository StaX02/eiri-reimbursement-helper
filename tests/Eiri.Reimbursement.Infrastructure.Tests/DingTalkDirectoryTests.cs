using System.Net;
using System.Text.Json;
using Eiri.Reimbursement.Core.DingTalk;
using Eiri.Reimbursement.Infrastructure.DingTalk;
using Eiri.Reimbursement.Infrastructure.Sqlite;

namespace Eiri.Reimbursement.Infrastructure.Tests;

public sealed class DingTalkDirectoryTests
{
    [Fact]
    public async Task DepartmentsStartAtRootAndTraverseAllChildrenWithoutDuplicates()
    {
        List<long> parents = [];
        using HttpClient http = new(new Handler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/topapi/v2/department/listsub", request.RequestUri!.AbsolutePath);
            Assert.Equal("?access_token=test%2Btoken", request.RequestUri.Query);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            long parent = body.RootElement.GetProperty("dept_id").GetInt64();
            parents.Add(parent);
            return Json(parent switch
            {
                1 => """{"errcode":0,"result":[{"dept_id":2,"name":"研发部"},{"dept_id":3,"name":"财务部"}]}""",
                2 => """{"errcode":0,"result":[{"dept_id":4,"name":"研发一组"}]}""",
                4 => """{"errcode":0,"result":[{"dept_id":2,"name":"研发部"}]}""",
                _ => """{"errcode":0,"result":[]}"""
            });
        }));
        var result = await new DingTalkDirectoryClient(http).GetDepartmentsAsync("test+token");
        Assert.Equal([1L, 2L, 3L, 4L], parents);
        Assert.Equal(["研发部", "财务部", "研发一组"], result.Select(item => item.Name));
    }

    [Fact]
    public async Task UserIdsAreResolvedThroughDetailsAndSameNamesKeepTheirIds()
    {
        List<string> queried = [];
        using HttpClient http = new(new Handler(async request =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            if (request.RequestUri!.AbsolutePath == "/topapi/user/listid")
            {
                Assert.Equal(42, body.RootElement.GetProperty("dept_id").GetInt64());
                return Json("""{"errcode":0,"result":{"userid_list":["first","second","first"]}}""");
            }
            Assert.Equal("/topapi/v2/user/get", request.RequestUri.AbsolutePath);
            queried.Add(body.RootElement.GetProperty("userid").GetString()!);
            return Json("""{"errcode":"0","result":{"name":"张三"}}""");
        }));
        var users = await new DingTalkDirectoryClient(http).GetUsersAsync("test-token", 42);
        Assert.Equal(["first", "second"], queried);
        Assert.Equal(["first", "second"], users.Select(user => user.UserId));
        Assert.All(users, user => Assert.Equal("张三", user.Name));
    }

    [Theory]
    [InlineData("{\"errcode\":40014,\"errmsg\":\"test-token\"}")]
    [InlineData("{\"errcode\":0,\"result\":{}}")]
    [InlineData("{\"result\":[]}")]
    [InlineData("not-json")]
    public async Task ApiErrorsAndInvalidDataAreReportedWithoutSecrets(string response)
    {
        using HttpClient http = new(new Handler(_ => Task.FromResult(Json(response))));
        var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => new DingTalkDirectoryClient(http).GetDepartmentsAsync("test-token"));
        Assert.DoesNotContain("test-token", error.Message);
    }

    [Fact]
    public async Task SubmissionInfoPersistsAndClearsWhenApplicationChanges()
    {
        string root = Path.Combine(Path.GetTempPath(), "eiri-dingtalk-tests", Guid.NewGuid().ToString("N"));
        try
        {
            SqliteReimbursementWorkspace workspace = new(root);
            await workspace.InitializeAsync();
            await workspace.SaveDingTalkConnectionAsync(new("app-one", "test-secret", "test-token"));
            DingTalkSubmissionInfo saved = new(new(42, "研发部"), new("first", "张三"));
            await workspace.SaveSubmissionInfoAsync(saved);
            SqliteReimbursementWorkspace reopened = new(root);
            await reopened.InitializeAsync();
            Assert.Equal(saved, await reopened.GetSubmissionInfoAsync());
            await reopened.SaveDingTalkConnectionAsync(new("app-one", "test-secret", "new-token"));
            Assert.Equal(saved, await reopened.GetSubmissionInfoAsync());
            await reopened.SaveDingTalkConnectionAsync(new("app-two", "other-secret", "other-token"));
            Assert.Equal(new(null, null), await reopened.GetSubmissionInfoAsync());
            await reopened.SaveSubmissionInfoAsync(saved);
            await reopened.ClearDingTalkConnectionAsync();
            Assert.Equal(new(null, null), await reopened.GetSubmissionInfoAsync());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
