using System.Net.Http.Json;
using System.Text.Json;
using Eiri.Reimbursement.Core.DingTalk;

namespace Eiri.Reimbursement.Infrastructure.DingTalk;

public sealed partial class DingTalkApprovalClient(HttpClient httpClient) : IDingTalkApprovalClient, IDingTalkApprovalStatusClient
{
    public async Task<DingTalkApprovalState> GetInstanceStatusAsync(string accessToken, string instanceId, CancellationToken cancellationToken = default)
    {
        var result = await GetInstanceResultAsync(accessToken, instanceId, cancellationToken);
        if (result.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String)
        {
            string? outcome = ReadString(result, "result");
            var state = new DingTalkApprovalState(status.GetString()!, status.GetString() == "COMPLETED" ? outcome : null, ReadString(result, "businessId"));
            if (state.IsValid) return state;
        }
        throw new InvalidOperationException("钉钉返回的审批流程状态或审批结果缺失或无法识别，请稍后刷新流程重试。");
    }

    private async Task<JsonElement> GetInstanceResultAsync(string accessToken, string instanceId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        using HttpRequestMessage message = new(HttpMethod.Get,
            "https://api.dingtalk.com/v1.0/workflow/processInstances?processInstanceId=" + Uri.EscapeDataString(instanceId));
        message.Headers.Add("x-acs-dingtalk-access-token", accessToken);
        using var response = await httpClient.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"获取审批流程失败（HTTP {(int)response.StatusCode}），请检查钉钉连接及工作流实例读权限后重试。");
        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object)
                return result.Clone();
        }
        catch (JsonException) { }
        throw new InvalidOperationException("钉钉返回的审批流程状态或审批结果缺失或无法识别，请稍后刷新流程重试。");
    }

    public async Task<string> CreateInstanceAsync(string accessToken, JsonElement request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        if (request.ValueKind != JsonValueKind.Object) throw new ArgumentException("审批请求必须为 JSON 对象。", nameof(request));
        using HttpRequestMessage message = new(HttpMethod.Post, "https://api.dingtalk.com/v1.0/workflow/processInstances");
        message.Headers.Add("x-acs-dingtalk-access-token", accessToken);
        message.Content = JsonContent.Create(request);
        using var response = await httpClient.SendAsync(message, cancellationToken);
        int status = (int)response.StatusCode;
        if (status is >= 400 and < 500 && status != 408)
            throw new DingTalkApprovalRejectedException($"钉钉拒绝提交（HTTP {status}），请检查令牌、工作流写权限及表单内容。");
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"提交结果不明（HTTP {status}），请先到钉钉核对审批记录。");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (json.RootElement.ValueKind != JsonValueKind.Object
            || !json.RootElement.TryGetProperty("instanceId", out var id)
            || id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString()))
            throw new InvalidOperationException("返回内容缺少审批实例 ID，请先到钉钉核对审批记录。");
        return id.GetString()!;
    }
}
