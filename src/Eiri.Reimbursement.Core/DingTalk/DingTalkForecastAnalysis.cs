using System.Text.Json;

namespace Eiri.Reimbursement.Core.DingTalk;

public sealed record DingTalkForecastAnalysis(bool? HasSelfSelectedNodes, IReadOnlyList<string> NodeNames)
{
    public static DingTalkForecastAnalysis Analyze(JsonElement response)
    {
        if (response.ValueKind != JsonValueKind.Object || !response.TryGetProperty("result", out var result)
            || result.ValueKind != JsonValueKind.Object
            || !result.TryGetProperty("isForecastSuccess", out var success) || success.ValueKind != JsonValueKind.True
            || !result.TryGetProperty("workflowActivityRules", out var rules) || rules.ValueKind != JsonValueKind.Array)
            return new(null, []);

        List<string> names = [];
        bool incomplete = false;
        foreach (var rule in rules.EnumerateArray())
        {
            if (rule.ValueKind != JsonValueKind.Object) { incomplete = true; continue; }
            string? type = rule.TryGetProperty("activityType", out var activityType) && activityType.ValueKind == JsonValueKind.String
                ? activityType.GetString() : null;
            bool? selected = rule.TryGetProperty("isTargetSelect", out var flag) && flag.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? flag.GetBoolean() : null;
            if (type == "target_select" || selected == true)
            {
                string? name = rule.TryGetProperty("activityName", out var label) && label.ValueKind == JsonValueKind.String ? label.GetString() : null;
                names.Add(string.IsNullOrWhiteSpace(name) ? "未命名节点" : name);
            }
            else if (type != "target_approval" && selected != false) incomplete = true;
        }
        return new(names.Count > 0 ? true : incomplete ? null : false, names);
    }
}
