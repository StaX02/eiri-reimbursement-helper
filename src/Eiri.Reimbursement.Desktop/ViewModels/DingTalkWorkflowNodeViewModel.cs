using System.Text.Json;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Eiri.Reimbursement.Core.DingTalk;

namespace Eiri.Reimbursement.Desktop.ViewModels;

public sealed class DingTalkWorkflowCandidate
{
    public string UserId { get; }
    public string DisplayName { get; }
    public DingTalkWorkflowCandidate(string id, string name) { UserId = id; DisplayName = $"{name} ({id})"; }
}

public sealed partial class DingTalkWorkflowNodeViewModel : ObservableObject
{
    public string Name { get; }
    public string ActorKey { get; }
    public bool IsUnkeyedNotifier { get; }
    public bool IsSelectable { get; }
    public bool Required { get; }
    public bool AllowedMulti { get; }
    public bool UsesDepartment { get; }
    public bool CanAdd => IsSelectable && AllowedMulti;
    public bool IsBusy => People.Any(p => p.IsBusy);
    public string Details { get; }
    public IReadOnlyList<DingTalkWorkflowCandidate> Candidates { get; }
    private readonly ObservableCollection<DingTalkWorkflowPersonViewModel> _people = [];
    public ReadOnlyObservableCollection<DingTalkWorkflowPersonViewModel> People { get; }
    private readonly IDingTalkDirectoryClient? _directory;
    private readonly string _token;
    public event Action? Changed;

    private static string Text(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString()! : "";
    private static bool Flag(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.True;

    public DingTalkWorkflowNodeViewModel(JsonElement rule, IDingTalkDirectoryClient? directory = null, string token = "")
    {
        _directory = directory; _token = token; People = new(_people);
        var actor = rule.TryGetProperty("workflowActor", out var a) && a.ValueKind == JsonValueKind.Object ? a : default;
        IsSelectable = Flag(rule, "isTargetSelect") || Text(rule, "activityType") == "target_select";
        bool hasRequired = actor.TryGetPropertySafe("required", out var required) || rule.TryGetPropertySafe("required", out required);
        Required = IsSelectable && (!hasRequired || required.ValueKind != JsonValueKind.False);
        AllowedMulti = Flag(actor, "allowedMulti");
        ActorKey = Text(actor, "actorKey");
        IsUnkeyedNotifier = Text(actor, "actorType") == "notifier" && string.IsNullOrWhiteSpace(ActorKey);
        Name = (Text(rule, "activityName") is { Length: > 0 } name ? name : "流程节点") + (Required ? "*" : "");
        List<DingTalkWorkflowCandidate> candidates = [];
        bool hasRange = actor.TryGetPropertySafe("actorSelectionRange", out var range);
        UsesDepartment = !hasRange || range.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            || (range.ValueKind == JsonValueKind.Object && !range.EnumerateObject().Any());
        if (hasRange
            && range.TryGetPropertySafe("approvals", out var approvals) && approvals.ValueKind == JsonValueKind.Array)
            foreach (var person in approvals.EnumerateArray())
            {
                var id = Text(person, "workNo");
                if (!string.IsNullOrWhiteSpace(id) && candidates.All(c => c.UserId != id))
                    candidates.Add(new(id, Text(person, "userName")));
            }
        Candidates = candidates.AsReadOnly();
        if (IsSelectable && rule.TryGetPropertySafe("activityActioners", out var defaults) && defaults.ValueKind == JsonValueKind.Array)
        {
            foreach (var person in defaults.EnumerateArray().Take(AllowedMulti ? int.MaxValue : 1))
            {
                var id = Text(person, "userId");
                if (string.IsNullOrWhiteSpace(id) || People.Any(p => p.SelectedCandidate?.UserId == id)) continue;
                var candidate = UsesDepartment ? new DingTalkWorkflowCandidate(id, Text(person, "name"))
                    : Candidates.FirstOrDefault(c => c.UserId == id);
                if (candidate is null) continue;
                AddPersonCore(candidate);
            }
        }
        if (IsSelectable && People.Count == 0) AddPersonCore(null);
        string method = Text(actor, "approvalMethod") switch { "AND" => "会签", "OR" => "或签", "ONE_BY_ONE" => "依次审批", _ => "" };
        var assigned = rule.TryGetPropertySafe("activityActioners", out var actioners) && actioners.ValueKind == JsonValueKind.Array
            ? string.Join("、", actioners.EnumerateArray().Select(p => Text(p, "name") + " (" + Text(p, "userId") + ")")) : "";
        Details = IsSelectable
            ? !UsesDepartment && Candidates.Count == 0 ? "候选范围未包含可选人员。" : AllowedMulti ? "可添加多位人员" : "请选择一位人员"
            : assigned.Length > 0 ? assigned : Text(actor, "approvalType") switch { "AUTO_AGREE" => "自动同意", "AUTO_REFUSE" => "自动拒绝", _ => "按模板执行" };
        if (method.Length > 0) Details += " · " + method;
    }

    private void AddPersonCore(DingTalkWorkflowCandidate? selected)
    {
        var row = new DingTalkWorkflowPersonViewModel(UsesDepartment, AllowedMulti,
            UsesDepartment ? selected is null ? [] : new[] { selected } : Candidates, selected, _directory, _token);
        row.Changed += () => { if (_people.Contains(row)) Changed?.Invoke(); };
        _people.Add(row); Changed?.Invoke();
    }
    public void AddPerson() { if (CanAdd) AddPersonCore(null); }
    public void RemovePerson(DingTalkWorkflowPersonViewModel row)
    {
        if (!AllowedMulti || !_people.Contains(row)) return;
        row.Remove(); _people.Remove(row); Changed?.Invoke();
    }

    public string[] SelectedUserIds()
    {
        if (IsBusy) throw new InvalidOperationException($"“{Name}”正在获取人员信息。");
        var selected = People.Where(p => p.SelectedCandidate is not null).Select(p => p.SelectedCandidate!).ToArray();
        if (People.Any(p => p.SelectedCandidate is { } c && !p.Candidates.Contains(c))) throw new InvalidOperationException($"“{Name}”所选人员不在候选范围内。");
        if (selected.Select(c => c.UserId).Distinct().Count() != selected.Length) throw new InvalidOperationException($"“{Name}”存在重复人员。");
        if (AllowedMulti && selected.Length > 0 && selected.Length != People.Count) throw new InvalidOperationException($"请完成“{Name}”的人员选择或删除空白条目。");
        if (Required && selected.Length == 0) throw new InvalidOperationException($"请为“{Name}”选择人员。" + (UsesDepartment ? "请选择部门及对应人员。" : Candidates.Count == 0 ? "流程未返回具体候选人员。" : ""));
        if (selected.Length > 0 && string.IsNullOrWhiteSpace(ActorKey) && !IsUnkeyedNotifier) throw new InvalidOperationException($"“{Name}”缺少节点标识，请重新获取流程。");
        return selected.Select(c => c.UserId).ToArray();
    }
}

internal static class WorkflowJsonExtensions
{
    public static bool TryGetPropertySafe(this JsonElement value, string key, out JsonElement item)
    {
        item = default;
        return value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out item);
    }
}
