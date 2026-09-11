using System.Net;
using Eiri.Reimbursement.Infrastructure.DingTalk;
using Eiri.Reimbursement.Infrastructure.Sqlite;
using Eiri.Reimbursement.Core.Orders;
using Eiri.Reimbursement.Core.DingTalk;

namespace Eiri.Reimbursement.Infrastructure.Tests;

public sealed class DingTalkApprovalStatusTests
{
    [Theory]
    [InlineData("agree", "已同意", false)]
    [InlineData("refuse", "已拒绝", true)]
    public async Task VersionElevenCompletedRecordsFetchMissingResultAfterMigration(string result, string display, bool canResubmit)
    {
        string root = Path.Combine(Path.GetTempPath(), "eiri-result-migration", Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = new SqliteReimbursementWorkspace(root); await workspace.InitializeAsync();
            var id = await workspace.CreateReimbursementAsync([await workspace.CreateOrderAsync(new(OrderPlatform.JD))]);
            await workspace.BeginApprovalSubmissionAsync(id);
            await workspace.CompleteApprovalSubmissionAsync(id, "legacy-instance");
            await workspace.SaveApprovalStatusAsync(id, "legacy-instance", "COMPLETED", result);
            await using (var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(root, "library.db")};Pooling=False"))
            {
                await db.OpenAsync();
                await using var sql = db.CreateCommand();
                sql.CommandText = "ALTER TABLE dingtalk_approval_submissions DROP COLUMN result; ALTER TABLE dingtalk_approval_submissions DROP COLUMN pending; ALTER TABLE dingtalk_approval_submissions DROP COLUMN business_id; PRAGMA user_version = 11;";
                await sql.ExecuteNonQueryAsync();
            }
            var reopened = new SqliteReimbursementWorkspace(root); await reopened.InitializeAsync();
            Assert.Equal("结果待获取", (await reopened.GetReimbursementAsync(id))!.Form.ApprovalStatusDisplay);
            Assert.False((await reopened.GetReimbursementAsync(id))!.Form.CanResubmitApproval);
            Assert.Equal(id, Assert.Single(await reopened.ListApprovalStatusCandidatesAsync()).Id);
            Assert.True(await reopened.SaveApprovalStatusAsync(id, "legacy-instance", "COMPLETED", result));
            var form = (await reopened.GetReimbursementAsync(id))!.Form;
            Assert.Equal(display, form.ApprovalStatusDisplay);
            Assert.Equal(result, form.DingTalkApprovalResult);
            Assert.Equal(canResubmit, form.CanResubmitApproval);
            Assert.Empty(await reopened.ListApprovalStatusCandidatesAsync());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task RefreshCandidatesExcludeUnsubmittedArchivedAndCompletedAndPersistOnReopen()
    {
        string root = Path.Combine(Path.GetTempPath(), "eiri-status", Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = new SqliteReimbursementWorkspace(root);
            await workspace.InitializeAsync();
            async Task<Guid> Create(bool submitted = true)
            {
                var id = await workspace.CreateReimbursementAsync([await workspace.CreateOrderAsync(new(OrderPlatform.JD))]);
                if (submitted) { await workspace.BeginApprovalSubmissionAsync(id); await workspace.CompleteApprovalSubmissionAsync(id, id.ToString()); }
                return id;
            }
            var running = await Create();
            var terminated = await Create();
            var completed = await Create();
            var archived = await Create();
            var unsubmitted = await Create();
            var manual = await Create(false);
            await workspace.SaveApprovalStatusAsync(running, running.ToString(), "RUNNING");
            await workspace.SaveApprovalStatusAsync(terminated, terminated.ToString(), "TERMINATED");
            await workspace.SaveApprovalStatusAsync(completed, completed.ToString(), "COMPLETED", "agree");
            await workspace.SetReimbursementMilestonesAsync([
                new(archived, Milestone.Exported, DateTimeOffset.UtcNow), new(archived, Milestone.Refunded, DateTimeOffset.UtcNow),
                new(unsubmitted, Milestone.Submitted, null), new(manual, Milestone.Submitted, DateTimeOffset.UtcNow)]);
            var reopened = new SqliteReimbursementWorkspace(root);
            await reopened.InitializeAsync();
            Assert.Equal(new[] { running, terminated }.Order(), (await reopened.ListApprovalStatusCandidatesAsync()).Select(c => c.Id).Order());
            Assert.Equal("已撤销", (await reopened.GetReimbursementAsync(terminated))!.Form.ApprovalStatusDisplay);
            Assert.Equal("已同意", (await reopened.ListReimbursementsAsync()).Single(f => f.Id == completed).ApprovalStatusDisplay);
            Assert.Equal("未提交", (await reopened.GetReimbursementAsync(unsubmitted))!.Form.ApprovalStatusDisplay);
            Assert.False(await reopened.SaveApprovalStatusAsync(archived, archived.ToString(), "COMPLETED", "agree"));
            Assert.False(await reopened.SaveApprovalStatusAsync(unsubmitted, unsubmitted.ToString(), "RUNNING"));
            Assert.False(await reopened.SaveApprovalStatusAsync(running, "stale-instance", "COMPLETED", "agree"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("RUNNING", "refuse", "审批中")]
    [InlineData("TERMINATED", "agree", "已撤销")]
    [InlineData("COMPLETED", "agree", "已同意")]
    [InlineData("COMPLETED", "refuse", "已拒绝")]
    public async Task ReadsStatusDirectlyFromResult(string status, string result, string display)
    {
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://api.dingtalk.com/v1.0/workflow/processInstances?processInstanceId=instance%26one", request.RequestUri!.AbsoluteUri);
            Assert.Equal("token", Assert.Single(request.Headers.GetValues("x-acs-dingtalk-access-token")));
            return new(HttpStatusCode.OK) { Content = new StringContent($$$"""{"status":"wrong","result":{"businessId":"202609110001","status":"{{{status}}}","result":"{{{result}}}"}}""") };
        }));
        var actual = await new DingTalkApprovalClient(http).GetInstanceStatusAsync("token", "instance&one");
        Assert.Equal(new DingTalkApprovalState(status, status == "COMPLETED" ? result : null, "202609110001"), actual);
        Assert.Equal(display, actual.DisplayName);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(send(request));
    }

    [Theory]
    [InlineData(403, "private-token")]
    [InlineData(500, "private-token")]
    [InlineData(200, "invalid-json")]
    [InlineData(200, "{\"status\":\"COMPLETED\"}")]
    [InlineData(200, "{\"result\":{\"status\":\"UNKNOWN\"}}")]
    [InlineData(200, "{\"result\":{\"status\":null}}")]
    [InlineData(200, "{\"result\":{\"status\":\"COMPLETED\"}}")]
    [InlineData(200, "{\"result\":{\"status\":\"COMPLETED\",\"result\":\"unknown\"}}")]
    [InlineData(200, "{\"result\":{\"status\":\"COMPLETED\",\"result\":null}}")]
    public async Task FailedOrInvalidResponsesHaveActionableErrorsWithoutResponseSecrets(int status, string body)
    {
        using var http = new HttpClient(new Handler(_ => new((HttpStatusCode)status) { Content = new StringContent(body) }));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new DingTalkApprovalClient(http).GetInstanceStatusAsync("private-token", "instance"));
        Assert.Contains("重试", error.Message);
        Assert.DoesNotContain("private-token", error.Message);
    }
}
