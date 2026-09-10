using System.IO;
using Eiri.Reimbursement.Core.DingTalk;
using Eiri.Reimbursement.Core.Orders;
using Eiri.Reimbursement.Desktop.ViewModels;
using Eiri.Reimbursement.Infrastructure.Sqlite;

namespace Eiri.Reimbursement.Desktop.Tests;

public sealed class ApprovalStatusRefreshTests
{
    [Fact]
    public async Task StartupRefreshesAllCandidatesAndManualRefreshPreservesSelectionAndInvalidInput()
    {
        string root = Path.Combine(Path.GetTempPath(), "eiri-refresh", Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = new SqliteReimbursementWorkspace(root);
            await workspace.InitializeAsync();
            await workspace.SaveDingTalkConnectionAsync(new("client", "secret", "token", DateTimeOffset.UtcNow.AddHours(1)));
            for (int i = 0; i < 101; i++)
            {
                var id = await workspace.CreateReimbursementAsync([await workspace.CreateOrderAsync(new(OrderPlatform.JD))]);
                await workspace.BeginApprovalSubmissionAsync(id);
                await workspace.CompleteApprovalSubmissionAsync(id, id.ToString());
            }
            var client = new StatusClient();
            var vm = new MainWindowViewModel(workspace, approvalStatusClient: client);
            await vm.LoadAsync();
            Assert.Equal(101, client.Calls);
            Assert.All(await workspace.ListReimbursementsAsync(limit: 500), f => Assert.Equal("审批中", f.ApprovalStatusDisplay));
            var selected = vm.Reimbursements[0];
            await vm.SetSelectedReimbursementsAsync([selected]);
            var editor = vm.ReimbursementEditor!;
            editor.TotalAmount = "invalid amount";
            Assert.False(await editor.PendingSave);
            client.Status = "COMPLETED";
            client.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task refresh = vm.RefreshApprovalStatusesAsync();
            Assert.False(vm.RefreshApprovalStatusesCommand.CanExecute(null));
            await vm.RefreshApprovalStatusesAsync(); // A duplicate click must not start another batch.
            client.Gate.SetResult();
            await refresh;
            Assert.Equal(202, client.Calls);
            Assert.Same(selected, vm.SelectedReimbursement);
            Assert.Same(editor, vm.ReimbursementEditor);
            Assert.Equal("invalid amount", editor.TotalAmount);
            Assert.Equal("已同意", editor.ApprovalStatusDisplay);
            Assert.Equal("已同意", selected.Form.ApprovalStatusDisplay);
            await vm.RefreshApprovalStatusesAsync();
            Assert.Equal(202, client.Calls);
            Assert.Contains("没有需要刷新", vm.StatusMessage);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class StatusClient : IDingTalkApprovalStatusClient
    {
        public int Calls { get; private set; }
        public string Status { get; set; } = "RUNNING";
        public TaskCompletionSource? Gate { get; set; }
        public string? FailureInstance { get; set; }
        public async Task<DingTalkApprovalState> GetInstanceStatusAsync(string accessToken, string instanceId, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Gate is not null) await Gate.Task.WaitAsync(cancellationToken);
            if (instanceId == FailureInstance) throw new System.Net.Http.HttpRequestException("private-token");
            return new(Status, Status == "COMPLETED" ? "agree" : null);
        }
    }

    [Fact]
    public async Task FailurePreservesPreviousStatusContinuesOtherFormsAndCanRetry()
    {
        string root = Path.Combine(Path.GetTempPath(), "eiri-refresh-failure", Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = new SqliteReimbursementWorkspace(root);
            await workspace.InitializeAsync();
            await workspace.SaveDingTalkConnectionAsync(new("client", "secret", "token", DateTimeOffset.UtcNow.AddHours(1)));
            var failed = await workspace.CreateReimbursementAsync([await workspace.CreateOrderAsync(new(OrderPlatform.JD))]);
            var success = await workspace.CreateReimbursementAsync([await workspace.CreateOrderAsync(new(OrderPlatform.JD))]);
            foreach (var id in new[] { failed, success })
            {
                await workspace.BeginApprovalSubmissionAsync(id);
                await workspace.CompleteApprovalSubmissionAsync(id, id.ToString());
                await workspace.SaveApprovalStatusAsync(id, id.ToString(), "RUNNING");
            }
            var client = new StatusClient { Status = "TERMINATED", FailureInstance = failed.ToString() };
            var vm = new MainWindowViewModel(workspace, approvalStatusClient: client);
            await vm.LoadAsync();
            Assert.Equal("RUNNING", (await workspace.GetReimbursementAsync(failed))!.Form.DingTalkApprovalStatus);
            Assert.Equal("TERMINATED", (await workspace.GetReimbursementAsync(success))!.Form.DingTalkApprovalStatus);
            Assert.Contains("成功 1 个，失败 1 个", vm.StatusMessage);
            Assert.Contains("网络连接失败", vm.StatusMessage);
            Assert.DoesNotContain("private-token", vm.StatusMessage);
            Assert.True(vm.RefreshApprovalStatusesCommand.CanExecute(null));
            client.FailureInstance = null;
            await vm.RefreshApprovalStatusesAsync();
            Assert.Equal("TERMINATED", (await workspace.GetReimbursementAsync(failed))!.Form.DingTalkApprovalStatus);
            var archived = new MainWindowViewModel(workspace, isArchive: true, approvalStatusClient: client);
            await archived.LoadAsync();
            Assert.False(archived.RefreshApprovalStatusesCommand.CanExecute(null));
            Assert.Equal(4, client.Calls);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task MissingOrExpiredConnectionAndCancellationLeaveRefreshRetryable()
    {
        string root = Path.Combine(Path.GetTempPath(), "eiri-refresh-cancel", Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = new SqliteReimbursementWorkspace(root);
            await workspace.InitializeAsync();
            var id = await workspace.CreateReimbursementAsync([await workspace.CreateOrderAsync(new(OrderPlatform.JD))]);
            await workspace.BeginApprovalSubmissionAsync(id);
            await workspace.CompleteApprovalSubmissionAsync(id, "instance");
            var client = new StatusClient();
            var vm = new MainWindowViewModel(workspace, approvalStatusClient: client);
            await vm.LoadAsync();
            Assert.Contains("连接接口", vm.StatusMessage);
            await workspace.SaveDingTalkConnectionAsync(new("client", "secret", "token", DateTimeOffset.UtcNow.AddMinutes(-1)));
            await vm.RefreshApprovalStatusesAsync();
            Assert.Contains("已过期", vm.StatusMessage);
            Assert.Equal(0, client.Calls);
            await workspace.SaveDingTalkConnectionAsync(new("client", "secret", "token", DateTimeOffset.UtcNow.AddHours(1)));
            client.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cancellation = new CancellationTokenSource();
            var pending = vm.RefreshApprovalStatusesAsync(cancellation.Token);
            cancellation.Cancel();
            await pending;
            Assert.Contains("已取消", vm.StatusMessage);
            Assert.True(vm.RefreshApprovalStatusesCommand.CanExecute(null));
            Assert.Null((await workspace.GetReimbursementAsync(id))!.Form.DingTalkApprovalStatus);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
