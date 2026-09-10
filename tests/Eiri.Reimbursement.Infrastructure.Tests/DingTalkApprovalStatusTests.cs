using System.Net;
using Eiri.Reimbursement.Infrastructure.DingTalk;
using Eiri.Reimbursement.Infrastructure.Sqlite;
using Eiri.Reimbursement.Core.Orders;

namespace Eiri.Reimbursement.Infrastructure.Tests;

public sealed class DingTalkApprovalStatusTests
{
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
            await workspace.SaveApprovalStatusAsync(completed, completed.ToString(), "COMPLETED");
            await workspace.SetReimbursementMilestonesAsync([
                new(archived, Milestone.Exported, DateTimeOffset.UtcNow), new(archived, Milestone.Refunded, DateTimeOffset.UtcNow),
                new(unsubmitted, Milestone.Submitted, null), new(manual, Milestone.Submitted, DateTimeOffset.UtcNow)]);
            var reopened = new SqliteReimbursementWorkspace(root);
            await reopened.InitializeAsync();
            Assert.Equal(new[] { running, terminated }.Order(), (await reopened.ListApprovalStatusCandidatesAsync()).Select(c => c.Id).Order());
            Assert.Equal("已撤销", (await reopened.GetReimbursementAsync(terminated))!.Form.ApprovalStatusDisplay);
            Assert.Equal("审批完成", (await reopened.ListReimbursementsAsync()).Single(f => f.Id == completed).ApprovalStatusDisplay);
            Assert.Equal("未提交", (await reopened.GetReimbursementAsync(unsubmitted))!.Form.ApprovalStatusDisplay);
            Assert.False(await reopened.SaveApprovalStatusAsync(archived, archived.ToString(), "COMPLETED"));
            Assert.False(await reopened.SaveApprovalStatusAsync(unsubmitted, unsubmitted.ToString(), "RUNNING"));
            Assert.False(await reopened.SaveApprovalStatusAsync(running, "stale-instance", "COMPLETED"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("RUNNING")]
    [InlineData("TERMINATED")]
    [InlineData("COMPLETED")]
    public async Task ReadsStatusDirectlyFromResult(string status)
    {
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://api.dingtalk.com/v1.0/workflow/processInstances?processInstanceId=instance%26one", request.RequestUri!.AbsoluteUri);
            Assert.Equal("token", Assert.Single(request.Headers.GetValues("x-acs-dingtalk-access-token")));
            return new(HttpStatusCode.OK) { Content = new StringContent($$$"""{"status":"wrong","result":{"status":"{{{status}}}","result":"refuse"}}""") };
        }));
        Assert.Equal(status, await new DingTalkApprovalClient(http).GetInstanceStatusAsync("token", "instance&one"));
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
    public async Task FailedOrInvalidResponsesHaveActionableErrorsWithoutResponseSecrets(int status, string body)
    {
        using var http = new HttpClient(new Handler(_ => new((HttpStatusCode)status) { Content = new StringContent(body) }));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new DingTalkApprovalClient(http).GetInstanceStatusAsync("private-token", "instance"));
        Assert.Contains("重试", error.Message);
        Assert.DoesNotContain("private-token", error.Message);
    }
}
