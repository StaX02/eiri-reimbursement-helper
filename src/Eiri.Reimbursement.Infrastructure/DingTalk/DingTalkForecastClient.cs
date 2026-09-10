using System.Net.Http.Json;
using System.Text.Json;
using Eiri.Reimbursement.Core.DingTalk;

namespace Eiri.Reimbursement.Infrastructure.DingTalk;

public sealed class DingTalkForecastClient(HttpClient httpClient) : IDingTalkForecastClient
{
    public async Task<JsonElement> ForecastAsync(string accessToken, DingTalkForecastRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        if (string.IsNullOrWhiteSpace(request.ProcessCode) || request.DeptId <= 0 || string.IsNullOrWhiteSpace(request.UserId)
            || request.FormComponentValues.Count > 150) throw new InvalidOperationException("流程请求参数不完整或表单超过 150 项。");
        using HttpRequestMessage message = new(HttpMethod.Post, "https://api.dingtalk.com/v1.0/workflow/processes/forecast");
        message.Headers.Add("x-acs-dingtalk-access-token", accessToken);
        message.Content = JsonContent.Create(request);
        using var response = await httpClient.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"获取流程失败（HTTP {(int)response.StatusCode}），请检查连接令牌、工作流实例写权限及表单内容。");
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (!document.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
                throw new JsonException();
            return document.RootElement.Clone();
        }
        catch (JsonException) { throw new InvalidOperationException("钉钉返回的流程数据格式无效，请重试。"); }
    }
}
