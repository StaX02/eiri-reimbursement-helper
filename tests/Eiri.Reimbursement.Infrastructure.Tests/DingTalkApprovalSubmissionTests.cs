using System.Net;
using System.Text.Json;
using Eiri.Reimbursement.Core.DingTalk;
using Eiri.Reimbursement.Core.Orders;
using Eiri.Reimbursement.Infrastructure.DingTalk;
using Eiri.Reimbursement.Infrastructure.Sqlite;

namespace Eiri.Reimbursement.Infrastructure.Tests;

public sealed class DingTalkApprovalSubmissionTests
{
    [Theory]
    [InlineData("RUNNING", null, false)]
    [InlineData("COMPLETED", "agree", false)]
    [InlineData("COMPLETED", "refuse", true)]
    [InlineData("TERMINATED", null, true)]
    public async Task RunningAgreedAndArchivedApprovalsCannotResubmit(string status, string? result, bool archived)
    {
        string root = Path.Combine(Path.GetTempPath(), "eiri-resubmit-guard", Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = new SqliteReimbursementWorkspace(root); await workspace.InitializeAsync();
            var id = await workspace.CreateReimbursementAsync([await workspace.CreateOrderAsync(new(OrderPlatform.JD))]);
            await workspace.BeginApprovalSubmissionAsync(id);
            await workspace.CompleteApprovalSubmissionAsync(id, "existing");
            await workspace.SaveApprovalStatusAsync(id, "existing", status, result);
            if (archived) await workspace.SetReimbursementMilestonesAsync([
                new(id, Milestone.Exported, DateTimeOffset.UtcNow), new(id, Milestone.Refunded, DateTimeOffset.UtcNow)]);
            Assert.False((await workspace.GetReimbursementAsync(id))!.Form.CanResubmitApproval);
            Assert.False(await workspace.BeginApprovalSubmissionAsync(id));
            Assert.Equal("existing", (await workspace.GetApprovalSubmissionAsync(id))!.InstanceId);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("COMPLETED", "refuse")]
    [InlineData("TERMINATED", null)]
    public async Task ResubmissionRetainsOldReceiptUntilSuccessAndLocksConcurrentAttempts(string status, string? result)
    {
        string root = Path.Combine(Path.GetTempPath(), "eiri-resubmit", Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = new SqliteReimbursementWorkspace(root); await workspace.InitializeAsync();
            var id = await workspace.CreateReimbursementAsync([await workspace.CreateOrderAsync(new(OrderPlatform.JD))]);
            Assert.True(await workspace.BeginApprovalSubmissionAsync(id));
            await workspace.CompleteApprovalSubmissionAsync(id, "old-instance");
            await workspace.SaveApprovalStatusAsync(id, "old-instance", status, result);
            Assert.True((await workspace.GetReimbursementAsync(id))!.Form.CanResubmitApproval);
            Assert.True(await workspace.BeginApprovalSubmissionAsync(id));
            Assert.False(await workspace.BeginApprovalSubmissionAsync(id));
            Assert.Null((await workspace.GetApprovalSubmissionAsync(id))!.InstanceId);
            Assert.Equal("old-instance", (await workspace.GetReimbursementAsync(id))!.Form.DingTalkInstanceId);
            Assert.Empty(await workspace.ListApprovalStatusCandidatesAsync());
            Assert.False(await workspace.SaveApprovalStatusAsync(id, "old-instance", "RUNNING"));
            var reopened = new SqliteReimbursementWorkspace(root); await reopened.InitializeAsync();
            Assert.Null((await reopened.GetApprovalSubmissionAsync(id))!.InstanceId);
            await reopened.ClearPendingApprovalSubmissionAsync(id);
            Assert.Equal("old-instance", (await reopened.GetApprovalSubmissionAsync(id))!.InstanceId);
            Assert.True(await reopened.BeginApprovalSubmissionAsync(id));
            await reopened.CompleteApprovalSubmissionAsync(id, "new-instance");
            var form = (await reopened.GetReimbursementAsync(id))!.Form;
            Assert.Equal("new-instance", form.DingTalkInstanceId);
            Assert.Null(form.DingTalkApprovalStatus);
            Assert.Null(form.DingTalkApprovalResult);
            Assert.False(form.CanResubmitApproval);
            Assert.False(await reopened.BeginApprovalSubmissionAsync(id));
            Assert.Equal("new-instance", Assert.Single(await reopened.ListApprovalStatusCandidatesAsync()).InstanceId);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task PostsExactRequestAndReadsInstanceIdWithoutLeakingToken()
    {
        using var json = JsonDocument.Parse("""{"originatorUserId":"u","processCode":"p","deptId":2,"formComponentValues":[{"name":"内容","value":"测试"}]}""");
        using var http = new HttpClient(new Handler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://api.dingtalk.com/v1.0/workflow/processInstances", request.RequestUri!.AbsoluteUri);
            Assert.Equal("secret", request.Headers.GetValues("x-acs-dingtalk-access-token").Single());
            var body = await request.Content!.ReadAsStringAsync();
            Assert.DoesNotContain("secret", body);
            using var actual = JsonDocument.Parse(body);
            Assert.True(JsonElement.DeepEquals(json.RootElement, actual.RootElement));
            return new(HttpStatusCode.OK) { Content = new StringContent("""{"instanceId":"instance-123"}""") };
        }));
        Assert.Equal("instance-123", await new DingTalkApprovalClient(http).CreateInstanceAsync("secret", json.RootElement));
    }

    [Theory]
    [InlineData(400, "private-token", true)]
    [InlineData(408, "private-token", false)]
    [InlineData(500, "private-token", false)]
    [InlineData(200, "{}", false)]
    [InlineData(200, "invalid-json", false)]
    public async Task DistinguishesRejectionFromUncertainOutcome(int status, string body, bool rejected)
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) })));
        var error = await Record.ExceptionAsync(() => new DingTalkApprovalClient(http).CreateInstanceAsync("secret", JsonSerializer.SerializeToElement(new { processCode = "p" })));
        Assert.NotNull(error);
        Assert.Equal(rejected, error is DingTalkApprovalRejectedException);
        Assert.DoesNotContain("private-token", error.Message); Assert.DoesNotContain("secret", error.Message);
    }

    [Fact]
    public async Task PendingReceiptSurvivesReopenAndCompletionAtomicallyMarksFormAndOrders()
    {
        string root = Path.Combine(Path.GetTempPath(), "eiri-submit-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = new SqliteReimbursementWorkspace(root); await workspace.InitializeAsync();
            var order = await workspace.CreateOrderAsync(new(OrderPlatform.Taobao));
            var id = await workspace.CreateReimbursementAsync([order]);
            Assert.True(await workspace.BeginApprovalSubmissionAsync(id));
            Assert.False(await workspace.BeginApprovalSubmissionAsync(id));
            var reopened = new SqliteReimbursementWorkspace(root); await reopened.InitializeAsync();
            Assert.NotNull(await reopened.GetApprovalSubmissionAsync(id));
            Assert.Null((await reopened.GetReimbursementAsync(id))!.Form.SubmittedAt);
            await reopened.ClearPendingApprovalSubmissionAsync(id);
            Assert.True(await reopened.BeginApprovalSubmissionAsync(id));
            await reopened.CompleteApprovalSubmissionAsync(id, "instance");
            Assert.Equal("instance", (await reopened.GetApprovalSubmissionAsync(id))!.InstanceId);
            Assert.NotNull((await reopened.GetReimbursementAsync(id))!.Form.SubmittedAt);
            Assert.NotNull((await reopened.SearchOrdersAsync(new())).Single().SubmittedAt);
            await reopened.ClearPendingApprovalSubmissionAsync(id);
            Assert.False(await reopened.BeginApprovalSubmissionAsync(id));
            Assert.Equal("instance", (await reopened.GetApprovalSubmissionAsync(id))!.InstanceId);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
