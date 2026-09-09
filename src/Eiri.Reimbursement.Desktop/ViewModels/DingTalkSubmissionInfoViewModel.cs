using CommunityToolkit.Mvvm.ComponentModel;
using Eiri.Reimbursement.Core.DingTalk;

namespace Eiri.Reimbursement.Desktop.ViewModels;

public sealed class DingTalkSubmissionInfoViewModel(IDingTalkDirectoryClient client, IDingTalkSubmissionInfoStore store, string accessToken) : ObservableObject
{
    private DingTalkSubmissionInfo _selection = new(null, null);
    private IReadOnlyList<DingTalkDepartment> _departments = [];
    private IReadOnlyList<DingTalkUser> _users = [];
    private bool _isBusy;
    private string _statusMessage = "";
    public IReadOnlyList<DingTalkDepartment> Departments { get => _departments; private set => SetProperty(ref _departments, value); }
    public IReadOnlyList<DingTalkUser> Users { get => _users; private set => SetProperty(ref _users, value); }
    public DingTalkDepartment? SelectedDepartment => _selection.Department;
    public DingTalkUser? SelectedUser => _selection.User;
    public bool CanSelectDepartment => !IsBusy;
    public bool CanSelectUser => !IsBusy && SelectedDepartment is not null;
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public bool IsBusy
    {
        get => _isBusy;
        private set { SetProperty(ref _isBusy, value); OnPropertyChanged(nameof(CanSelectDepartment)); OnPropertyChanged(nameof(CanSelectUser)); }
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) => RunAsync(async () =>
    {
        var saved = await store.GetSubmissionInfoAsync(cancellationToken);
        Departments = saved.Department is null ? [] : [saved.Department];
        Users = saved.User is null ? [] : [saved.User];
        SetSelection(saved);
    }, "", cancellationToken);

    public Task LoadDepartmentsAsync(CancellationToken cancellationToken = default) => RunAsync(async () =>
    {
        var departments = await client.GetDepartmentsAsync(accessToken, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var department = departments.FirstOrDefault(item => item.DeptId == SelectedDepartment?.DeptId);
        var updated = new DingTalkSubmissionInfo(department, department is null ? null : SelectedUser);
        await store.SaveSubmissionInfoAsync(updated, cancellationToken);
        Departments = departments;
        if (department is null) Users = [];
        SetSelection(updated);
        StatusMessage = departments.Count == 0 ? "未找到可访问的部门。" : "";
    }, "正在获取部门列表…", cancellationToken);

    public Task LoadUsersAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedDepartment is not { } department) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            var users = await client.GetUsersAsync(accessToken, department.DeptId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var user = users.FirstOrDefault(item => item.UserId == SelectedUser?.UserId);
            var updated = new DingTalkSubmissionInfo(department, user);
            await store.SaveSubmissionInfoAsync(updated, cancellationToken);
            Users = users;
            SetSelection(updated);
            StatusMessage = users.Count == 0 ? "该部门没有可选报销人。" : "";
        }, "正在获取报销人列表…", cancellationToken);
    }

    public Task SelectDepartmentAsync(DingTalkDepartment? department, CancellationToken cancellationToken = default)
    {
        if (department == SelectedDepartment || department is null || !Departments.Contains(department)) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            var updated = new DingTalkSubmissionInfo(department, null);
            await store.SaveSubmissionInfoAsync(updated, cancellationToken);
            Users = [];
            SetSelection(updated);
            StatusMessage = "已保存。";
        }, "正在保存…", cancellationToken);
    }

    public Task SelectUserAsync(DingTalkUser? user, CancellationToken cancellationToken = default)
    {
        if (SelectedDepartment is null || user is null || !Users.Contains(user) || user == SelectedUser) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            var updated = new DingTalkSubmissionInfo(SelectedDepartment, user);
            await store.SaveSubmissionInfoAsync(updated, cancellationToken);
            SetSelection(updated);
            StatusMessage = "已保存。";
        }, "正在保存…", cancellationToken);
    }

    private void SetSelection(DingTalkSubmissionInfo value)
    {
        _selection = value;
        OnPropertyChanged(nameof(SelectedDepartment));
        OnPropertyChanged(nameof(SelectedUser));
        OnPropertyChanged(nameof(CanSelectUser));
    }

    private async Task RunAsync(Func<Task> action, string status, CancellationToken cancellationToken)
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = status;
        try { await action(); }
        catch (OperationCanceledException) { if (!cancellationToken.IsCancellationRequested) StatusMessage = "请求超时，请重试。"; }
        catch (InvalidOperationException exception) { StatusMessage = exception.Message; }
        catch (Exception) { StatusMessage = "读取或保存失败，请检查网络与资料库访问权限后重试。"; }
        finally { IsBusy = false; SetSelection(_selection); }
    }
}
