# 钉钉接口连接

本切片获取企业内部应用 AccessToken，尚未提交报销单。

依据[钉钉官方文档及 C# 示例](https://open.dingtalk.com/document/development/obtain-the-access-token-of-an-internal-app)，客户端把 Client ID / Client Secret 映射为 `appKey` / `appSecret`，POST 到 `https://api.dingtalk.com/v1.0/oauth2/accessToken`，输出 `accessToken`。网络层使用 .NET HttpClient 实现同一请求契约，支持取消和 30 秒超时，禁用重定向，不记录请求或响应正文。

在“钉钉 → 连接接口”填写凭据后点击确定。成功获取令牌后原子替换连接记录，再显示返回令牌。失败保留原有记录。重新打开弹窗会填入上次保存的凭据；点击确定重新获取令牌，不把历史令牌当作仍然有效的凭证。官方有效期为 7200 秒，本切片不实现后台刷新。

数据库迁移到版本 6，增加单行 `dingtalk_connection` 表。默认存储位置为 `%LOCALAPPDATA%\EiriReimbursementHelper\library.db`；三个字段保存在本地 SQLite 数据库中，当前未加密。整库备份包含连接记录，恢复备份也会恢复该记录。“清除连接记录”删除当前数据库记录，不撤销钉钉端已签发的令牌，也不修改此前导出的备份。

数据库及受管资料库目录禁止上传 GitHub。`.gitignore` 排除 SQLite 数据库及边车文件、`EiriReimbursementHelper`、`originals`、`staging`、`cache` 目录和 `.eirbackup` / `.zip` 备份包。自定义资料库应放在仓库外；不使用 `git add -f` 绕过规则。

自动验证使用虚构凭据及模拟 HTTP 响应，不连接真实钉钉应用。真实连通性由用户在弹窗中使用自己的企业内部应用凭据验证。
