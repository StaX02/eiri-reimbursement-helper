using System.Text.Json;

namespace Eiri.Reimbursement.Core.DingTalk;

public sealed record DingTalkFormOption(string Key, string Value);
public sealed record DingTalkFormField(string Id, string Label, string ComponentName, IReadOnlyList<DingTalkFormOption> Options,
    bool Required = false, JsonElement? Schema = null);
public sealed record DingTalkFormTemplate(string ProcessCode, IReadOnlyList<DingTalkFormField> Fields, string? ApplicantFieldId)
{
    public IReadOnlyList<DingTalkFormField> AllFields { get; init; } = Fields;
}
public sealed record DingTalkPrefillValue(string ComponentName, string[] Values);

public interface IDingTalkFormClient
{
    Task<DingTalkFormTemplate> GetReimbursementTemplateAsync(string accessToken, CancellationToken cancellationToken = default);
}

public interface IDingTalkFormPrefillStore
{
    Task<IReadOnlyDictionary<string, DingTalkPrefillValue>> GetFormPrefillAsync(string processCode, CancellationToken cancellationToken = default);
    Task SaveFormPrefillAsync(string processCode, IReadOnlyDictionary<string, DingTalkPrefillValue> values, CancellationToken cancellationToken = default);
}

public static class DingTalkFormSchema
{
    public static DingTalkFormTemplate Parse(string processCode, JsonElement response)
    {
        JsonElement schema = response.GetProperty("result").GetProperty("schemaContent");
        if (schema.ValueKind == JsonValueKind.String)
        {
            using var document = JsonDocument.Parse(schema.GetString()!);
            schema = document.RootElement.Clone();
        }
        List<DingTalkFormField> fields = [];
        List<DingTalkFormField> allFields = [];
        HashSet<string> ids = [];
        string? applicant = null;
        foreach (var item in schema.GetProperty("items").EnumerateArray())
        {
            string type = item.GetProperty("componentName").GetString()!;
            var props = item.GetProperty("props");
            string label = props.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "";
            string id = props.GetProperty("id").GetString()!;
            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id)) throw new JsonException("Invalid form field identity.");
            List<DingTalkFormOption> options = [];
            if (type is "DDSelectField" or "DDMultiSelectField")
            {
                JsonElement source = props.TryGetProperty("options", out var choices) ? choices : props.GetProperty("objOptions");
                foreach (var option in source.EnumerateArray())
                {
                    JsonElement value = option;
                    if (value.ValueKind == JsonValueKind.String)
                    {
                        string text = value.GetString()!;
                        if (!text.TrimStart().StartsWith('{')) { options.Add(new(text, text)); continue; }
                        using var document = JsonDocument.Parse(text);
                        value = document.RootElement.Clone();
                    }
                    string textValue = value.GetProperty("value").GetString()!;
                    string key = value.TryGetProperty("key", out var k) ? k.GetString()! : textValue;
                    if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(textValue)) throw new JsonException();
                    options.Add(new(key, textValue));
                }
                if (options.Select(option => option.Key).Distinct().Count() != options.Count) throw new JsonException();
            }
            bool required = props.TryGetProperty("required", out var r) && r.ValueKind == JsonValueKind.True;
            var field = new DingTalkFormField(id, label, type, options, required, item.Clone());
            allFields.Add(field);
            if (props.TryGetProperty("hidden", out var hidden) && hidden.ValueKind == JsonValueKind.True) continue;
            if (label.Trim() is "报销类型" or "报销内容") continue;
            if (type == "InnerContactField" && label == "报销人") { applicant = id; continue; }
            // Only scalar inputs and static choices are suitable for reusable defaults.
            if (type is not ("TextField" or "TextareaField" or "NumberField" or "PhoneField" or "DDSelectField" or "DDMultiSelectField")) continue;
            if (new[] { "日期", "金额", "图片", "附件", "关联", "上传" }.Any(label.Contains)) continue;
            if (string.IsNullOrWhiteSpace(label)) throw new JsonException("Invalid form field identity.");
            fields.Add(field);
        }
        return new(processCode, fields, applicant) { AllFields = allFields };
    }
}
