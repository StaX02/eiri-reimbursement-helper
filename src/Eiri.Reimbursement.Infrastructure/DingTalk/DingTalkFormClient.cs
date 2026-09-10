using System.Text.Json;
using Eiri.Reimbursement.Core.DingTalk;

namespace Eiri.Reimbursement.Infrastructure.DingTalk;

public sealed class DingTalkFormClient(HttpClient httpClient) : IDingTalkFormClient
{
    public const string TemplateName = "日常报销（电子发票）";

    public async Task<DingTalkFormTemplate> GetReimbursementTemplateAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        try
        {
            var codeResponse = await GetAsync("processCentres/schemaNames/processCodes?name=" + Uri.EscapeDataString(TemplateName), accessToken, cancellationToken);
            string? code = codeResponse.GetProperty("result").GetProperty("processCode").GetString();
            if (string.IsNullOrWhiteSpace(code)) throw new JsonException();
            var schema = await GetAsync("forms/schemas/processCodes?processCode=" + Uri.EscapeDataString(code), accessToken, cancellationToken);
            return DingTalkFormSchema.Parse(code, schema);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException)
        { throw new InvalidOperationException("钉钉审批模板数据不完整，请检查模板后重试。"); }
    }

    private async Task<JsonElement> GetAsync(string path, string token, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        using HttpRequestMessage request = new(HttpMethod.Get, "https://api.dingtalk.com/v1.0/workflow/" + path);
        request.Headers.Add("x-acs-dingtalk-access-token", token);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"获取钉钉审批模板失败（HTTP {(int)response.StatusCode}），请检查模板名称、工作流模板读权限及连接令牌。");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.Clone();
    }
}
