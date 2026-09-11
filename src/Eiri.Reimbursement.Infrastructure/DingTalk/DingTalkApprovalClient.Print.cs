using System.Text.Json;
using Eiri.Reimbursement.Core.Export;

namespace Eiri.Reimbursement.Infrastructure.DingTalk;

public sealed partial class DingTalkApprovalClient : IApprovedReimbursementDetailsClient
{
    public async Task<ApprovedReimbursementPrintData> GetApprovedPrintDataAsync(string accessToken, string instanceId, CancellationToken cancellationToken = default)
    {
        var result = await GetInstanceResultAsync(accessToken, instanceId, cancellationToken);
        if (ReadString(result, "status") != "COMPLETED" || ReadString(result, "result") != "agree")
            throw new InvalidOperationException("钉钉审批尚未同意，请刷新流程后重试。");
        if (!result.TryGetProperty("formComponentValues", out var components) || components.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("钉钉审批详情缺少表单内容，请稍后重试。");
        Dictionary<string, string> fields = new(StringComparer.Ordinal);
        foreach (var component in components.EnumerateArray())
        {
            var name = ReadString(component, "name");
            if (!string.IsNullOrWhiteSpace(name)) fields[name] = ReadDisplayValue(ReadString(component, "value") ?? "");
        }
        string Field(params string[] names) => names.Select(n => fields.GetValueOrDefault(n)).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";
        string reimburser = Field("报销人");
        string number = ReadString(result, "businessId") ?? "";
        string department = Field("报销人所属部门", "创建人部门");
        if (string.IsNullOrWhiteSpace(department)) department = ReadString(result, "originatorDeptName") ?? "";
        if (string.IsNullOrWhiteSpace(reimburser) || string.IsNullOrWhiteSpace(number) || string.IsNullOrWhiteSpace(department))
            throw new InvalidOperationException("钉钉审批详情缺少审批编号、报销人或所属部门，请核对审批表单后重试。");
        return new(reimburser, [
            new("审批编号", number), new("创建人", reimburser), new("创建人部门", department),
            new("研究方向", Field("研究方向")), new("申请日期", Field("申请日期")), new("报销人", reimburser),
            new("报销类型", Field("报销类型")), new("报销内容", Field("报销内容")), new("总金额", Field("总金额（元）", "总金额")),
            new("收款人名称", Field("收款人名称")), new("收款人账号", Field("收款人账号（或工作证号）", "收款人账号")),
            new("开户行名称", Field("开户行名称")), new("备注", Field("备注"))]);
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string ReadDisplayValue(string value)
    {
        if (!value.TrimStart().StartsWith('[')) return value;
        try
        {
            using var json = JsonDocument.Parse(value);
            return string.Join("、", json.RootElement.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String
                ? item.GetString() : ReadString(item, "name") ?? ReadString(item, "value") ?? ""));
        }
        catch (JsonException) { return value; }
    }
}
