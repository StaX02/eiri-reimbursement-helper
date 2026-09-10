# 钉钉接口连接

本切片获取企业内部应用 AccessToken，尚未提交报销单。

## 报销提审信息

打开窗口时调用[获取模板 code](https://open.dingtalk.com/document/development/obtain-the-template-code)，按名称“日常报销（电子发票）”查询 `result.processCode`，随后调用[获取表单 schema](https://open.dingtalk.com/document/development/obtain-the-form-schema)。两个 GET 请求均通过 `x-acs-dingtalk-access-token` 请求头鉴权，需要工作流模板读权限。窗口加载失败可点击“重新加载”，不回退到示例模板。

从 `schemaContent.items` 构建预填字段：单选、多选、文本、多行文本、普通数字和电话；选择项兼容普通字符串、对象和 JSON 编码字符串。“报销类型”和“报销内容”按字段名称排除，不受控件类型影响。日期/日期区间、金额、图片、关联、文件附件、隐藏项和说明文字被排除；明细表及其他未知复合控件不作为静态默认值展开。模板的“报销人”联系人控件复用现有人员选择。样例生成研究方向、收款人名称、收款人账号、开户行名称和备注。

数据库版本 8 增加模板预填表，按 processCode 与控件 ID 保存控件类型、文本或选项 key；模板更新后仅恢复类型相同且仍存在的选项，字段删除后不再显示或写入。编辑自动保存，“保存”可重试；关闭等待保存完成。切换应用或清除连接同步清除模板预填值。`examples/form-schema` 只用于本地核对，不随程序发布、不加入 Git。

## 报销单流程预览

流程区域按 `result.workflowActivityRules` 返回顺序展示节点。`activityType=target_select` 或 `isTargetSelect=true` 的节点按 `allowedMulti` 显示审批人条目：false 固定一条，true 可添加和移除多条。非空 `workflowActor.actorSelectionRange` 仅允许从 `approvals`（`workNo`、`userName`）选择；空对象、null 或缺失范围时，每条提供部门和该部门人员选择，使用现有通讯录 API。切换部门清空旧人员。只有角色等非空范围时不扩大到通讯录。必选节点标注 `*`，指定人员节点只读展示。

提审窗口不展示调试 JSON，也不提供“获取流程”和“生成审批实例请求”按钮。默认字段及票据初始化完成后自动获取流程；研究方向或发起人/部门变化时刷新流程，连续变化会取消旧请求并合并刷新。其他字段修改保留流程人员选择，提交时重新校验并使用最新值。

默认人员来自节点 `activityActioners`：单选只取第一个人员，多选按返回顺序创建全部人员条目并按 userId 去重。有范围时以 userId 匹配候选人的 workNo，越界人员不选入；无范围时直接保留默认姓名和 userId，无需先知道其部门。替换默认人员时选择部门及成员。未给出默认人员时显示一条空白项；生成请求校验必选、重复人员及未填写的新增条目。

提交时内部生成 [发起审批实例](https://open.dingtalk.com/document/development/create-an-approval-instance) 请求体，读取当前表单值；自选人员通过 `targetSelectActioners`（`actorKey` 映射 `actionerKey`）传递，无 actorKey 的 notifier 节点使用 ccList，省略会覆盖模板的 approvers。校验必选、候选范围和最多 20 个自选节点。

“提交报销审批”发送校验后的请求体至 `POST /v1.0/workflow/processInstances`，仅通过 `x-acs-dingtalk-access-token` 请求头携带令牌。返回非空 instanceId 后，在本地事务中保存回执、设置报销单及所属订单的提交时间。提审窗口仅显示提交结果；实例 ID 作为报销单属性，在详情“报销单属性”末尾只读显示。复用版本 9 的 dingtalk_approval_submissions 回执表，无需复制实例 ID。发送前保存待核对记录，重复点击及重新打开已提交窗口不重复发送。明确 4xx 拒绝（不含 408）可重试；结果不明需核对，本地保存失败仅重试保存回执。测试使用模拟接口，未实际发起审批。

报销单详情侧栏的“钉钉提审”打开独立确认窗口。重新获取当前模板后，按完整 schema 顺序展示所有条目，`required: true` 在名称后显示 `*`；预填窗口仍使用筛选后的字段。恢复预填值时匹配控件 ID、类型和选项 key，确认窗口中的单选请求值使用选项名称，多选使用名称数组的 JSON 字符串。

申请日期默认为本机当天 `yyyy-MM-dd`；报销类型、内容和总金额取所选报销单，空值保持为空；备注在预填文本后追加关联订单的全部非空发票号码，以空格分隔。申请人及部门恢复预填记录，可在本次窗口重新选择，变化不覆盖全局预填。票据默认收集全部关联订单的发票 PDF 并转换所有页面，支持添加 PNG/JPEG/GIF/BMP 和移除图片。关联采购审批单、附件、发送到聊天展示为暂不处理；说明控件只读。其他复合控件保留完整 schema，并以文本/JSON 输入保留条目；当前样例中的基础控件已覆盖，复杂业务控件需结合模板另行扩展。

自动流程预测允许尚未填写的必填项，校验已填写的日期和数字并同步本地报销字段，再使用上传图片 URL 调用[流程预测接口](https://open.dingtalk.com/document/development/approval-process-prediction)。预测失败显示错误，使用“重试加载”重试；完整必填及人员校验在提交时执行，提交前再次同步本地字段。流程预测本身不创建审批实例。无候选范围的默认人员隐藏部门选择；单选提供“修改”展开部门，多选默认人员保留移除按钮，可新增替换人员。移除按钮与该条人员输入区域等高，不包含错误提示行。

### 图片媒体上传

图片加入列表时立即调用[上传媒体文件](https://open.dingtalk.com/document/development/upload-media-files)：`POST https://oapi.dingtalk.com/media/upload?type=image&access_token=...`，使用 multipart/form-data 的 `media` 文件字段、正确的 MIME 与文件长度。最多并行上传 3 张，图片限制为 20 MB；读取原始像素宽高并校验格式与扩展名一致。列表显示上传中、成功或具体失败原因，允许删除上传中及失败图片；删除只取消本地上传任务并移出列表，不删除用户原图，也不撤销已完成的钉钉媒体上传。

每个列表条目在当前提审窗口内保存原始 `media_id`、剥离所有前导非 ASCII 字母字符后的 ID、宽高、扩展名以及 URL。只处理前缀，保留 ID 后续字符；无法得到有效 ID 时视为失败。按用户指定规则构造 `https://static.dingtalk.com/media/{处理后的media_id}_{原始宽度}_{原始高度}.{小写扩展名}`（ID 按 URL 路径段编码）。获取流程时直接序列化成功条目的 URL，不再次上传；有上传中的图片时禁用获取流程，有失败图片时提示删除后重新添加。关闭窗口取消上传并忽略迟到响应，等待全部上传任务结束后清理本次转换缓存。元数据随当前窗口草稿保留，不写入全局预填；重新打开会重新转图上传。

已移除公网图片服务配置前提，不启动公共 HTTP 服务，不上传至第三方图床。官方上传文档提示媒体资源仅能在钉钉客户端使用；这里的 static.dingtalk.com URL 规则来自需求，尚未验证审批接口对该地址的可访问性。发票转换缓存仍位于系统临时目录的 `EiriReimbursementHelper/approval-images/<随机标识>`，数据库、真实图片及运行截图不上传 GitHub。

当前验证使用隔离数据库及模拟 API，覆盖媒体上传 multipart 请求、错误响应、20 MB 限制、ID 前缀清理、原始尺寸与 URL、添加即上传、删除与取消、失败后重新添加、流程复用 URL，以及完整表单和 WPF 渲染。尚未进行真实钉钉上传及图片 URL 下载联调。

通过“钉钉 → 报销提审信息”选择报销部门与报销人，选择后立即保存到同一数据库，供钉钉提审预填读取。部门保存 `dept_id` 与名称，报销人保存 `user_id` 与名称。

- [部门列表](https://open.dingtalk.com/document/development/obtain-the-department-list-v2)：POST `/topapi/v2/department/listsub`，从 `dept_id=1` 开始逐级获取全部可访问子部门。该 API 每次仅返回下一级部门。
- [部门用户 ID](https://open.dingtalk.com/document/development/query-the-list-of-department-userids)：POST `/topapi/user/listid`，传入所选部门 `dept_id`，读取 `result.userid_list`。
- [用户详情](https://open.dingtalk.com/document/development/query-user-details)：对每个不同的用户 ID 调用 POST `/topapi/v2/user/get`，请求字段 `userid`，显示 `result.name`。同名用户仍按独立 ID 保存。

通讯录接口使用现有 AccessToken；令牌失效时通过“连接接口”重新获取。应用需开通部门与成员信息读权限。加载失败不会保存部分查询结果；重新展开可重试。切换部门清空已选报销人，切换应用 Client ID 或清除连接记录会清除提审信息；同一应用刷新 Token 保留提审信息。数据库版本 7 新增 `dingtalk_submission_info`，随整库备份迁移，并受现有 Git 忽略规则保护。

依据[钉钉官方文档及 C# 示例](https://open.dingtalk.com/document/development/obtain-the-access-token-of-an-internal-app)，客户端把 Client ID / Client Secret 映射为 `appKey` / `appSecret`，POST 到 `https://api.dingtalk.com/v1.0/oauth2/accessToken`，输出 `accessToken`。网络层使用 .NET HttpClient 实现同一请求契约，支持取消和 30 秒超时，禁用重定向，不记录请求或响应正文。

点击“钉钉 → 连接接口”或主界面按钮区域左侧的连接状态，使用同一连接逻辑：数据库缺少 Client ID / Client Secret 时选择 JSON 文件，读取顶层非空字符串 appKey 和 appSecret；已有凭据时直接重新请求令牌。取消选择不覆盖旧记录。文件仅读取，不复制到资料库或项目目录，最大 1 MB；解析错误不回显原文件内容。获取失败弹窗提供“重新导入凭据”；成功后原子保存并更新主界面连接状态，不弹窗，悬停状态区域可查看到期时间。

接口响应中的 expireIn 按秒读取，验证为正整数后，以当前 UTC 时间加该秒数得到 ExpiresAt，不硬编码令牌有效期。数据库版本 10 为 dingtalk_connection 增加 expires_at。旧连接没有过期时间时显示“连接过期”，需重新获取令牌。主界面状态每秒检查到期时间：连接成功绿色、未连接灰色、连接错误红色、连接过期橙色；状态文字可点击重连，悬停显示到期时间。启动、清除连接及恢复资料库时刷新状态，不在后台自动请求令牌。

数据库版本 6 增加单行 `dingtalk_connection` 表，版本 10 增加过期时间。默认存储位置为 `%LOCALAPPDATA%\EiriReimbursementHelper\library.db`；凭据、令牌及过期时间保存在本地 SQLite 数据库中，当前未加密。整库备份包含连接记录，恢复备份也会恢复该记录。“清除连接记录”删除当前数据库记录，不撤销钉钉端已签发的令牌，也不修改此前导出的备份。

数据库及受管资料库目录禁止上传 GitHub。`.gitignore` 排除 SQLite 数据库及边车文件、`EiriReimbursementHelper`、`originals`、`staging`、`cache` 目录和 `.eirbackup` / `.zip` 备份包。自定义资料库应放在仓库外；不使用 `git add -f` 绕过规则。

自动验证使用虚构凭据及模拟 HTTP 响应，不连接真实钉钉应用。真实连通性由用户在弹窗中使用自己的企业内部应用凭据验证。

## 审批流程状态

使用[获取单个审批实例详情](https://open.dingtalk.com/document/development/obtains-the-details-of-a-single-approval-instance-pop)接口：GET `https://api.dingtalk.com/v1.0/workflow/processInstances`，查询参数 `processInstanceId` 为已保存的实例 ID，请求头 `x-acs-dingtalk-access-token` 使用当前连接令牌。应用需要工作流实例读权限。直接读取 `result.status`：`RUNNING` 显示“审批中”、`TERMINATED` 显示“已撤销”、`COMPLETED` 显示“审批完成”；审批结果 `result.result` 不参与此映射。

数据库版本 11 在 `dingtalk_approval_submissions` 中增加可空的 `status`，保存最近一次成功查询的原始值，随整库备份恢复。报销单未提交时显示“未提交”；已提交但缺少实例 ID 显示“无审批实例”，有实例但未查询成功显示“待获取”。

软件启动及两个“刷新流程”按钮均查询全库符合条件的报销单：未归档、已提交、具有实例 ID、尚未审批完成，包含当前页之外的记录。已撤销仍参与刷新。保存前重新检查候选条件与实例 ID，避免迟到响应更新已经归档或取消提交的记录。刷新不改变已提交、已返款等里程碑。

刷新过程中禁用重复操作，关闭窗口取消请求；单项失败保留上次状态并继续其他条目，结果在主窗口状态区显示成功、失败和跳过数量及失败条目。令牌缺失、过期或无到期时间时提示先通过“钉钉 → 连接接口”更新连接。验证使用隔离 SQLite、模拟 HTTP 响应和 WPF 窗口，尚未使用真实审批实例联调。
