using System.Text.Json;
using Eiri.Reimbursement.Core.DingTalk;
using Eiri.Reimbursement.Desktop.ViewModels;
using Microsoft.Data.Sqlite;

namespace Eiri.Reimbursement.Desktop.Tests;

public sealed class DingTalkSubmitTests
{
    [Theory]
    [InlineData("COMPLETED", "refuse")]
    [InlineData("TERMINATED", null)]
    public async Task RejectedOrWithdrawnApprovalCanSubmitNewInstance(string status, string? result)
    {
        using var context = new ApprovalTestContext();
        await context.Workspace.BeginApprovalSubmissionAsync(context.Id);
        await context.Workspace.CompleteApprovalSubmissionAsync(context.Id, "old-instance");
        await context.Workspace.SaveApprovalStatusAsync(context.Id, "old-instance", status, result);
        var client = new Client();
        var vm = context.Create(approval: client);
        await Prepare(vm);
        Assert.True(vm.CanSubmit);
        Assert.Empty(vm.InstanceId);
        await vm.SubmitAsync();
        Assert.Equal(1, client.Calls);
        Assert.Equal("instance-test", (await context.Workspace.GetReimbursementAsync(context.Id))!.Form.DingTalkInstanceId);
        Assert.False(vm.CanSubmit);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedResubmissionKeepsOldReceiptAndUnknownOutcomeRequiresVerification(bool rejectedRequest)
    {
        using var context = new ApprovalTestContext();
        await context.Workspace.BeginApprovalSubmissionAsync(context.Id);
        await context.Workspace.CompleteApprovalSubmissionAsync(context.Id, "old-instance");
        await context.Workspace.SaveApprovalStatusAsync(context.Id, "old-instance", "COMPLETED", "refuse");
        var client = new Client { Error = rejectedRequest ? new DingTalkApprovalRejectedException("拒绝请求") : new System.Net.Http.HttpRequestException() };
        var vm = context.Create(approval: client); await Prepare(vm); await vm.SubmitAsync();
        Assert.Equal("old-instance", (await context.Workspace.GetReimbursementAsync(context.Id))!.Form.DingTalkInstanceId);
        Assert.Empty(vm.InstanceId);
        Assert.False(vm.CanSaveSubmission);
        Assert.Equal(rejectedRequest, vm.CanSubmit);
        Assert.Equal(!rejectedRequest, vm.CanResetSubmission);
        var reopened = context.Create(approval: client); await reopened.LoadAsync();
        if (!rejectedRequest)
        {
            Assert.True(reopened.CanResetSubmission);
            Assert.False(reopened.CanSubmit);
            await reopened.ResetSubmissionAfterVerificationAsync();
        }
        Assert.True(reopened.CanForecast);
        Assert.Equal("old-instance", (await context.Workspace.GetApprovalSubmissionAsync(context.Id))!.InstanceId);
    }

    [Fact]
    public async Task AutomaticFlowInitializesRefreshesDirectionAndSubmitsLatestFields()
    {
        using var context = new ApprovalTestContext();
        var client = new Client(); var vm = context.Create(approval: client);
        vm.EnableAutomaticFlow(default); await vm.LoadAsync();
        Assert.Equal(1, context.Forecast.Calls); Assert.Single(vm.WorkflowNodes);
        context.Field(vm, "报销内容").SetText("自动流程后的新内容");
        Assert.Equal(1, context.Forecast.Calls); Assert.Single(vm.WorkflowNodes);
        var direction = context.Field(vm, "研究方向").Input;
        var original = direction.SelectedChoice;
        direction.SelectedChoice = null; direction.SelectedChoice = original;
        await vm.PendingAutomaticFlow;
        Assert.Equal(2, context.Forecast.Calls);
        vm.WorkflowNodes[0].People[0].SelectedCandidate = vm.WorkflowNodes[0].Candidates[0];
        context.Field(vm, "报销内容").SetText("提交前最新内容");
        Assert.True(vm.CanSubmit); await vm.SubmitAsync();
        Assert.Equal(1, client.Calls);
        Assert.Contains(client.Request.GetProperty("formComponentValues").EnumerateArray(), f => f.GetProperty("value").GetString() == "提交前最新内容");
        var form = (await context.Workspace.GetReimbursementAsync(context.Id))!.Form;
        Assert.Equal("提交前最新内容", form.Content); Assert.Equal("instance-test", form.DingTalkInstanceId);
        var editor = new ReimbursementEditorViewModel(context.Workspace, context.Id); await editor.LoadAsync();
        Assert.Equal("instance-test", editor.DingTalkInstanceId);
    }

    [Fact]
    public async Task AutomaticPreviewAllowsMissingRequiredValueButSubmitRejectsIt()
    {
        using var context = new ApprovalTestContext();
        await context.Workspace.UpdateReimbursementAsync(new(context.Id, null, "材料费", "", 1234));
        var client = new Client(); var vm = context.Create(approval: client);
        vm.EnableAutomaticFlow(default); await vm.LoadAsync();
        Assert.Single(vm.WorkflowNodes);
        vm.WorkflowNodes[0].People[0].SelectedCandidate = vm.WorkflowNodes[0].Candidates[0];
        await vm.SubmitAsync(); Assert.Equal(0, client.Calls); Assert.Contains("报销内容", vm.StatusMessage);
    }
    [Fact]
    public async Task ReceiptSaveFailureRetainsInstanceAndRetryDoesNotSendAgain()
    {
        using var context = new ApprovalTestContext();
        var client = new Client(); var vm = context.Create(approval: client); await Prepare(vm);
        await using var connection = new SqliteConnection($"Data Source={System.IO.Path.Combine(context.Root, "library.db")};Pooling=False");
        await connection.OpenAsync();
        await using var sql = connection.CreateCommand();
        sql.CommandText = "CREATE TRIGGER fail_receipt BEFORE UPDATE ON dingtalk_approval_submissions BEGIN SELECT RAISE(ABORT, 'test failure'); END;";
        await sql.ExecuteNonQueryAsync();
        await vm.SubmitAsync();
        Assert.Equal("instance-test", vm.InstanceId); Assert.True(vm.CanSaveSubmission);
        Assert.False(vm.CanResetSubmission); Assert.False(vm.CanSubmit);
        Assert.Null((await context.Workspace.GetReimbursementAsync(context.Id))!.Form.SubmittedAt);
        sql.CommandText = "DROP TRIGGER fail_receipt;"; await sql.ExecuteNonQueryAsync();
        await vm.SaveSubmissionReceiptAsync();
        Assert.False(vm.CanSaveSubmission); Assert.Equal(1, client.Calls);
        Assert.NotNull((await context.Workspace.GetReimbursementAsync(context.Id))!.Form.SubmittedAt);
    }

    [Fact]
    public async Task FailureToSavePendingRecordNeverSendsRequest()
    {
        using var context = new ApprovalTestContext();
        var client = new Client(); var vm = context.Create(approval: client); await Prepare(vm);
        await using var connection = new SqliteConnection($"Data Source={System.IO.Path.Combine(context.Root, "library.db")};Pooling=False");
        await connection.OpenAsync(); await using var sql = connection.CreateCommand();
        sql.CommandText = "CREATE TRIGGER fail_pending BEFORE INSERT ON dingtalk_approval_submissions BEGIN SELECT RAISE(ABORT, 'test failure'); END;";
        await sql.ExecuteNonQueryAsync();
        await vm.SubmitAsync();
        Assert.Equal(0, client.Calls); Assert.True(vm.CanSubmit);
        Assert.Contains("未发送", vm.SubmissionStatus);
    }
    internal sealed class Client : IDingTalkApprovalClient
    {
        public int Calls { get; private set; }
        public Exception? Error { get; set; }
        public bool Wait { get; set; }
        public TaskCompletionSource Release { get; } = new();
        public JsonElement Request { get; private set; }
        public async Task<string> CreateInstanceAsync(string accessToken, JsonElement request, CancellationToken cancellationToken = default)
        {
            Calls++; Request = request.Clone();
            if (Wait) await Release.Task.WaitAsync(cancellationToken);
            if (Error is { } error) throw error;
            return "instance-test";
        }
    }

    internal static async Task Prepare(DingTalkApprovalViewModel vm)
    {
        await vm.LoadAsync(); await vm.GetFlowAsync();
        vm.WorkflowNodes[0].People[0].SelectedCandidate = vm.WorkflowNodes[0].Candidates[0];
        vm.GenerateInstanceRequest(); Assert.NotEmpty(vm.InstanceRequestJson);
    }

    [Fact]
    public async Task SuccessBlocksRepeatedClicksAndPersistsReceiptAcrossWindows()
    {
        using var context = new ApprovalTestContext();
        var client = new Client { Wait = true }; var vm = context.Create(approval: client);
        await Prepare(vm);
        var submit = vm.SubmitAsync();
        Assert.True(vm.IsSubmitting); Assert.False(vm.CanSubmit); Assert.False(vm.CanEdit);
        await vm.SubmitAsync(); Assert.Equal(1, client.Calls);
        client.Release.SetResult(); await submit;
        Assert.Equal("instance-test", vm.InstanceId); Assert.False(vm.CanSubmit);
        Assert.NotNull((await context.Workspace.GetReimbursementAsync(context.Id))!.Form.SubmittedAt);
        var reopened = context.Create(approval: client); await reopened.LoadAsync();
        Assert.Equal("instance-test", reopened.InstanceId); Assert.False(reopened.CanSubmit);
        Assert.False(reopened.CanResetSubmission);
    }

    [Fact]
    public async Task RejectionAllowsRetryAndEditsRequireNewRequest()
    {
        using var context = new ApprovalTestContext();
        var client = new Client { Error = new DingTalkApprovalRejectedException("权限不足") };
        var vm = context.Create(approval: client); await Prepare(vm);
        await vm.SubmitAsync();
        Assert.True(vm.CanSubmit); Assert.Null(await context.Workspace.GetApprovalSubmissionAsync(context.Id));
        Assert.Null((await context.Workspace.GetReimbursementAsync(context.Id))!.Form.SubmittedAt);
        context.Field(vm, "报销内容").SetText("修改");
        await vm.SubmitAsync(); Assert.Equal(1, client.Calls); Assert.False(vm.CanSubmit);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownOutcomePersistsUntilExplicitVerification(bool canceled)
    {
        using var context = new ApprovalTestContext();
        var client = new Client { Error = canceled ? new OperationCanceledException() : new System.Net.Http.HttpRequestException() };
        var vm = context.Create(approval: client); await Prepare(vm); await vm.SubmitAsync();
        Assert.True(vm.CanResetSubmission); Assert.False(vm.CanSubmit);
        await vm.SubmitAsync(); Assert.Equal(1, client.Calls);
        var reopened = context.Create(approval: client); await reopened.LoadAsync();
        Assert.True(reopened.CanResetSubmission); Assert.False(reopened.CanSubmit);
        await reopened.ResetSubmissionAfterVerificationAsync();
        Assert.Null(await context.Workspace.GetApprovalSubmissionAsync(context.Id));
        Assert.False(reopened.CanSubmit); Assert.True(reopened.CanForecast);
    }
}
