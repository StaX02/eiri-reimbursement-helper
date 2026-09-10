using System.Net.Http.Json;
using System.Text.Json;
using Eiri.Reimbursement.Core.DingTalk;

namespace Eiri.Reimbursement.Infrastructure.DingTalk;

public sealed class DingTalkDirectoryClient(HttpClient httpClient) : IDingTalkDirectoryClient
{
    public async Task<IReadOnlyList<DingTalkDepartment>> GetDepartmentsAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        List<DingTalkDepartment> departments = [];
        Queue<long> pending = new([1]);
        HashSet<long> visited = [1];
        while (pending.TryDequeue(out long parent))
        {
            JsonElement result = await PostAsync("v2/department/listsub", accessToken, new { dept_id = parent, language = "zh_CN" }, cancellationToken);
            try
            {
                foreach (JsonElement item in result.EnumerateArray())
                {
                    long id = item.GetProperty("dept_id").GetInt64();
                    string name = ReadName(item);
                    if (id <= 0) throw new JsonException();
                    if (visited.Add(id)) { departments.Add(new(id, name)); pending.Enqueue(id); }
                }
            }
            catch (Exception ex) when (IsInvalidJson(ex)) { throw InvalidResponse(); }
        }
        return departments;
    }

    public async Task<IReadOnlyList<DingTalkUser>> GetUsersAsync(string accessToken, long deptId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(deptId, 1);
        JsonElement result = await PostAsync("user/listid", accessToken, new { dept_id = deptId }, cancellationToken);
        string[] ids;
        try
        {
            ids = result.GetProperty("userid_list").EnumerateArray().Select(id => id.GetString()).Select(id =>
                string.IsNullOrWhiteSpace(id) ? throw new JsonException() : id).Distinct(StringComparer.Ordinal).ToArray();
        }
        catch (Exception ex) when (IsInvalidJson(ex)) { throw InvalidResponse(); }
        List<DingTalkUser> users = [];
        foreach (string id in ids)
        {
            JsonElement user = await PostAsync("v2/user/get", accessToken, new { userid = id, language = "zh_CN" }, cancellationToken);
            try { users.Add(new(id, ReadName(user))); }
            catch (Exception ex) when (IsInvalidJson(ex)) { throw InvalidResponse(); }
        }
        return users;
    }

    private async Task<JsonElement> PostAsync(string path, string token, object body, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            $"https://oapi.dingtalk.com/topapi/{path}?access_token={Uri.EscapeDataString(token)}", body, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"钉钉通讯录请求失败（HTTP {(int)response.StatusCode}），请检查网络及应用权限后重试。");
        try
        {
            using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            JsonElement root = document.RootElement;
            JsonElement code = root.GetProperty("errcode");
            if (!int.TryParse(code.ToString(), out int errorCode)) throw new JsonException();
            if (errorCode != 0)
                throw new DingTalkDirectoryException(errorCode);
            return root.GetProperty("result").Clone();
        }
        catch (Exception ex) when (IsInvalidJson(ex)) { throw InvalidResponse(); }
    }

    private static string ReadName(JsonElement item) => item.GetProperty("name").GetString() is string name && !string.IsNullOrWhiteSpace(name) ? name : throw new JsonException();
    private static bool IsInvalidJson(Exception ex) => ex is JsonException or KeyNotFoundException or InvalidOperationException && ex is not DingTalkDirectoryException;
    private static InvalidOperationException InvalidResponse() => new("钉钉通讯录返回的数据无效，请重试。");
}

public sealed class DingTalkDirectoryException(int errorCode) : InvalidOperationException(
    $"钉钉通讯录查询失败（错误码 {errorCode}），请检查部门/成员读取权限；令牌失效时请重新连接接口。");
