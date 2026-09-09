using CommunityToolkit.Mvvm.ComponentModel;
using Eiri.Reimbursement.Core.DingTalk;

namespace Eiri.Reimbursement.Desktop.ViewModels;

public sealed partial class DingTalkWorkflowPersonViewModel : ObservableObject
{
    private readonly IDingTalkDirectoryClient? _directory;
    private readonly string _token;
    private bool _removed;
    public bool UsesDepartment { get; }
    public bool CanRemove { get; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDepartment))]
    [NotifyPropertyChangedFor(nameof(CanModify))]
    private bool _isDefaultPerson;
    public bool ShowDepartment => UsesDepartment && !IsDefaultPerson;
    public bool CanModify => IsDefaultPerson && !CanRemove;
    public void ModifyDefault() { if (CanModify && !IsBusy) IsDefaultPerson = false; }
    public bool CanSelectDepartment => UsesDepartment && !IsBusy;
    public bool CanSelectPerson => !IsBusy && (!UsesDepartment || SelectedDepartment is not null);
    [ObservableProperty] private IReadOnlyList<DingTalkWorkflowCandidate> _candidates;
    [ObservableProperty] private IReadOnlyList<DingTalkDepartment> _departments = [];
    [ObservableProperty] private DingTalkDepartment? _selectedDepartment;
    [ObservableProperty] private DingTalkWorkflowCandidate? _selectedCandidate;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSelectDepartment))]
    [NotifyPropertyChangedFor(nameof(CanSelectPerson))]
    private bool _isBusy;
    public event Action? Changed;

    public DingTalkWorkflowPersonViewModel(bool usesDepartment, bool canRemove,
        IReadOnlyList<DingTalkWorkflowCandidate> candidates, DingTalkWorkflowCandidate? selected,
        IDingTalkDirectoryClient? directory, string token)
    {
        UsesDepartment = usesDepartment; CanRemove = canRemove;
        _candidates = candidates; _selectedCandidate = selected; _directory = directory; _token = token;
        _isDefaultPerson = usesDepartment && selected is not null;
    }

    partial void OnSelectedCandidateChanged(DingTalkWorkflowCandidate? value) => Changed?.Invoke();
    partial void OnIsBusyChanged(bool value) => Changed?.Invoke();
    partial void OnSelectedDepartmentChanged(DingTalkDepartment? value)
    {
        SelectedCandidate = null; Candidates = []; StatusMessage = "";
        OnPropertyChanged(nameof(CanSelectPerson)); Changed?.Invoke();
    }
    public void Remove() => _removed = true;

    public async Task LoadDepartmentsAsync(CancellationToken cancellationToken)
    {
        if (!CanSelectDepartment || _removed) return;
        await RunAsync(async () =>
        {
            var departments = await _directory!.GetDepartmentsAsync(_token, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!_removed) { Departments = departments; StatusMessage = departments.Count == 0 ? "未找到可访问的部门。" : ""; }
        }, cancellationToken);
    }

    public async Task LoadPeopleAsync(CancellationToken cancellationToken)
    {
        if (!UsesDepartment || !CanSelectPerson || SelectedDepartment is not { } department || _removed) return;
        if (!Departments.Contains(department)) { StatusMessage = "请选择列表中的部门。"; return; }
        await RunAsync(async () =>
        {
            var users = await _directory!.GetUsersAsync(_token, department.DeptId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_removed || SelectedDepartment?.DeptId != department.DeptId) return;
            var selectedId = SelectedCandidate?.UserId;
            Candidates = users.Select(u => new DingTalkWorkflowCandidate(u.UserId, u.Name)).ToArray();
            SelectedCandidate = Candidates.FirstOrDefault(c => c.UserId == selectedId);
            StatusMessage = Candidates.Count == 0 ? "该部门没有可选人员。" : "";
        }, cancellationToken);
    }

    private async Task RunAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        IsBusy = true; StatusMessage = "正在获取人员信息…";
        try
        {
            if (_directory is null) throw new InvalidOperationException("通讯录服务不可用。");
            await action();
        }
        catch (OperationCanceledException) { if (!cancellationToken.IsCancellationRequested) StatusMessage = "请求超时，请重试。"; }
        catch (InvalidOperationException ex) { StatusMessage = ex.Message; }
        catch (Exception) { StatusMessage = "获取人员信息失败，请检查网络与通讯录权限后重试。"; }
        finally { IsBusy = false; }
    }
}
