using Eiri.Reimbursement.Core.DingTalk;
using System.IO;
using Eiri.Reimbursement.Desktop.ViewModels;

namespace Eiri.Reimbursement.Desktop.Tests;

public sealed class DingTalkSubmissionInfoViewModelTests
{
    [Fact]
    public async Task SelectionIsSavedRestoredAndChangingDepartmentClearsUser()
    {
        Store store = new();
        DirectoryClient client = new();
        DingTalkSubmissionInfoViewModel vm = new(client, store, "test-token");
        await vm.InitializeAsync();
        Assert.False(vm.CanSelectUser);
        Assert.Equal(0, client.DepartmentCalls);
        await vm.LoadDepartmentsAsync();
        await vm.SelectDepartmentAsync(vm.Departments[0]);
        Assert.True(vm.CanSelectUser);
        Assert.Empty(vm.Users);
        await vm.LoadUsersAsync();
        Assert.Equal(2, client.LastDepartment);
        await vm.SelectUserAsync(vm.Users[0]);
        Assert.Equal("first", store.Value.User!.UserId);
        DingTalkSubmissionInfoViewModel reopened = new(client, store, "test-token");
        await reopened.InitializeAsync();
        Assert.Equal(vm.SelectedDepartment, reopened.SelectedDepartment);
        Assert.Equal(vm.SelectedUser, reopened.SelectedUser);
        await vm.SelectDepartmentAsync(vm.Departments[1]);
        Assert.Null(vm.SelectedUser);
        Assert.Empty(vm.Users);
        Assert.Null(store.Value.User);
        Assert.Equal(3, store.Value.Department!.DeptId);
    }

    [Fact]
    public async Task FailedSavePreservesPreviousSelectionAndReportsError()
    {
        Store store = new();
        DingTalkSubmissionInfoViewModel vm = new(new DirectoryClient(), store, "test-token");
        await vm.LoadDepartmentsAsync();
        await vm.SelectDepartmentAsync(vm.Departments[0]);
        store.FailSave = true;
        await vm.SelectDepartmentAsync(vm.Departments[1]);
        Assert.Equal(2, vm.SelectedDepartment!.DeptId);
        Assert.NotEmpty(vm.StatusMessage);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task ClosingDuringLoadDoesNotWriteLateResponseOrEnableUserWithoutDepartment()
    {
        var response = new TaskCompletionSource<IReadOnlyList<DingTalkDepartment>>();
        DirectoryClient client = new() { PendingDepartments = response.Task };
        Store store = new();
        DingTalkSubmissionInfoViewModel vm = new(client, store, "test-token");
        using CancellationTokenSource cancel = new();
        Task pending = vm.LoadDepartmentsAsync(cancel.Token);
        Assert.True(vm.IsBusy);
        Assert.False(vm.CanSelectUser);
        await vm.LoadDepartmentsAsync(cancel.Token);
        Assert.Equal(1, client.DepartmentCalls);
        cancel.Cancel();
        response.SetResult([new(2, "研发部")]);
        await pending;
        Assert.Equal(0, store.SaveCalls);
        Assert.Empty(vm.Departments);
    }

    private sealed class Store : IDingTalkSubmissionInfoStore
    {
        public DingTalkSubmissionInfo Value = new(null, null);
        public bool FailSave;
        public int SaveCalls;
        public Task<DingTalkSubmissionInfo> GetSubmissionInfoAsync(CancellationToken cancellationToken = default) => Task.FromResult(Value);
        public Task SaveSubmissionInfoAsync(DingTalkSubmissionInfo info, CancellationToken cancellationToken = default)
        {
            if (FailSave) throw new IOException("Storage unavailable");
            SaveCalls++;
            Value = info;
            return Task.CompletedTask;
        }
    }

    private sealed class DirectoryClient : IDingTalkDirectoryClient
    {
        public int DepartmentCalls;
        public long LastDepartment;
        public Task<IReadOnlyList<DingTalkDepartment>>? PendingDepartments;
        public Task<IReadOnlyList<DingTalkDepartment>> GetDepartmentsAsync(string accessToken, CancellationToken cancellationToken = default)
        {
            DepartmentCalls++;
            return PendingDepartments ?? Task.FromResult<IReadOnlyList<DingTalkDepartment>>([new(2, "研发部"), new(3, "财务部")]);
        }
        public Task<IReadOnlyList<DingTalkUser>> GetUsersAsync(string accessToken, long deptId, CancellationToken cancellationToken = default)
        {
            LastDepartment = deptId;
            return Task.FromResult<IReadOnlyList<DingTalkUser>>([new("first", "张三")]);
        }
    }
}
