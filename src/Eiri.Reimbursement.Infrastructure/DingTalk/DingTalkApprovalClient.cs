using System.Net.Http.Json;
using System.Text.Json;
using Eiri.Reimbursement.Core.DingTalk;

namespace Eiri.Reimbursement.Infrastructure.DingTalk;

public sealed class DingTalkApprovalClient(HttpClient httpClient) : IDingTalkApprovalClient
{
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
