# 发票报销助手架构

本文描述当前源码与工作区实现；当前增量见[未发布变更](./release-notes/unreleased.md)，已发布版本的内容及验证结果见 [v0.5.0 发布记录](./release-notes/v0.5.0.md)。领域用语以 [CONTEXT.md](../CONTEXT.md) 为准；下一阶段的范围与验收条件见[开发计划](./development-plan.md)。

## 1. 目标与范围

应用面向 Windows 单人报销整理：手动创建订单，导入原始发票 PDF 和订单截图等辅助材料，提取并校正发票字段，将订单关联到报销单，跟踪已导出、已提交、已返款三个里程碑，并提供归档、批量导出和整库备份恢复。

资料库和识别处理位于本机。使用钉钉功能时，应用会联网获取模板、通讯录和审批信息，并上传提审图片、发送表单数据；连接凭据与令牌保存在本地数据库，备份包包含这些记录。详见 [钉钉接口说明](./dingtalk-connection.md)。

容量与识别准确率的待验收目标、暂缓功能统一记录在[开发计划](./development-plan.md)。

## 2. 技术栈与交付

| 区域 | 当前实现 |
| --- | --- |
| 主应用 | .NET 10、C#、WPF、CommunityToolkit.Mvvm |
| 数据库 | SQLite、Microsoft.Data.Sqlite、显式 SQL migration |
| 文件管理 | System.IO、SHA-256、受管资料库 |
| PDF 文本与转图 | 独立 Python worker、pypdfium2 / PDFium |
| 本地 OCR | RapidOCR 3.9.2、ONNX Runtime 1.29.0、随包 PP-OCRv6 small 模型 |
| 普通导出 | C# 生成 UTF-8 BOM CSV，worker 转图，复制原始材料 |
| 审批 PDF | worker 使用 ReportLab 生成首页、pypdf 拼接原始 PDF、Pillow 处理图片 |
| 备份 | SQLite 一致快照、System.IO.Compression ZIP、SHA-256 清单 |
| 测试 | xUnit、临时 SQLite、Python unittest、WPF 窗口与绑定冒烟测试 |
| 发布 | win-x64 self-contained 应用、WiX Toolset 6 MSI、免安装 ZIP；内置 Python、OCR 依赖与模型 |

当前普通导出不提供 XLSX 工作簿或可选合并 PDF；早期规划中的 ClosedXML、PDFsharp 和 qpdf 未用于当前实现。审批 PDF 是独立的已实现导出流程。

技术选型见 [ADR-0004](./adr/0004-dotnet-wpf-desktop-stack.md)、[ADR-0005](./adr/0005-isolated-document-worker.md)；MSI 交付见 [ADR-0006](./adr/0006-wix-msi-delivery.md)，该决策已替代早期 MSIX 方案。

## 3. 模块与接口

Core 定义领域记录和接口，Infrastructure 实现 SQLite、文件处理、导出、备份及钉钉客户端，Desktop 负责窗口、视图模型和操作编排。Python worker 处理文档任务。

| 边界 | 职责与源码入口 |
| --- | --- |
| 订单工作区 | [IReimbursementWorkspace](../src/Eiri.Reimbursement.Core/IReimbursementWorkspace.cs)：创建、查询、材料导入、发票分析与校正、里程碑及删除 |
| 报销单工作区 | [IReimbursementFormWorkspace](../src/Eiri.Reimbursement.Core/Reimbursements/ReimbursementContracts.cs)：订单关联、报销单属性、附件及里程碑 |
| 文档分析 | [IDocumentProcessor](../src/Eiri.Reimbursement.Core/Documents/IDocumentProcessor.cs) 的 `AnalyzeAsync(DocumentJob, CancellationToken)` |
| 普通导出 | [IReimbursementBatchExporter](../src/Eiri.Reimbursement.Core/Export/ExportContracts.cs) 的 `ExportAsync` |
| 审批 PDF | [审批 PDF 接口](../src/Eiri.Reimbursement.Core/Export/ApprovedReimbursementExport.cs)、[ApprovedReimbursementExporter](../src/Eiri.Reimbursement.Infrastructure/Export/ApprovedReimbursementExporter.cs) |
| 整库备份 | [IWholeLibraryBackupService](../src/Eiri.Reimbursement.Core/DataTransfer/IWholeLibraryBackupService.cs)、[WholeLibraryBackupService](../src/Eiri.Reimbursement.Infrastructure/DataTransfer/WholeLibraryBackupService.cs) |
| 钉钉 | [接口与数据契约](../src/Eiri.Reimbursement.Core/DingTalk)、[连接及提审说明](./dingtalk-connection.md) |

`SqliteReimbursementWorkspace` 通过多个接口提供持久化能力。导出和备份由独立服务承接，桌面层组织成功后的里程碑更新。

worker 使用版本 1 JSON Lines 协议，通过 stdin/stdout 接收单次任务。分析响应包含文本块、候选字段、来源、页码、坐标、置信度、解析器版本和 `needsReview`。转图及审批 PDF 有独立操作。协议与运行方式见 [worker README](../worker/document-worker/README.md)。

## 4. 数据模型

数据库迁移的权威来源是 [Schema.cs](../src/Eiri.Reimbursement.Infrastructure/Sqlite/Schema.cs)，当前 schema 版本为 14。

| 表 | 内容与约束 |
| --- | --- |
| `orders` | 平台、外部订单号、备注、三个里程碑、创建与更新时间；`reimbursement_id` 可空，每个订单最多关联一份报销单 |
| `managed_files` | 订单材料的角色、原文件名、相对路径、类型、大小、SHA-256、处理状态和错误；SHA-256 在该表内唯一 |
| `invoices` | 商家名称、字符串发票号、有符号整数分金额、CNY、人工核对标记和人工校正标记；发票号不设唯一约束 |
| `invoice_lines` | 顺序、名称、可空金额、`is_effective`；自动提取保留识别出的明细行，不保证自动排除折扣行 |
| `extraction_results` | 每份发票当前的机器候选、worker/parser 版本、完成时间及错误，不保存提取历史 |
| `reimbursement_forms` | 申请日期、类型、内容、可空总金额、分析标记和三个里程碑 |
| `reimbursement_files` | 报销单附件及文件元数据；同一报销单内按内容哈希去重 |
| `dingtalk_connection` | 应用凭据、令牌、到期时间、上次连接失败标记 |
| `dingtalk_submission_info` / `dingtalk_form_prefill` | 报销部门、报销人及按模板保存的预填值 |
| `dingtalk_approval_submissions` | 审批实例 ID、状态、结果、审批编号和待核对标记 |

金额持久化为整数分。人工校正后，重分析仍更新机器候选，但不会覆盖这张发票已保存的字段和明细。归档由三个里程碑全部完成推导，不增加独立归档字段。

订单列表中的金额来自发票求和；发票明细取第一张发票的第一条有效明细，单张发票多条时附加“等 N 条”（N 为剩余条数），多张发票时附加“等”。订单未关联报销单时显示“暂未绑定”，关联后以报销内容标识所属报销单。审批流程状态与报销里程碑分别保存，刷新审批信息不会自动设置已返款。

## 5. 受管资料库与设置

资料库默认位于 `%LOCALAPPDATA%\EiriReimbursementHelper`，可在安装时选择其他位置。升级与修复沿用既有路径；迁移使用整库备份恢复。受管文件在数据库中保存相对路径。

```text
library.db
originals/
  orders/{order-id}/supporting-materials/{file-id}.{ext}
  orders/{order-id}/invoices/{file-id}.pdf
  reimbursements/{reimbursement-id}/{file-id}.{ext}
cache/
staging/
logs/
```

导入会复制原始文件，后续用户移动或删除下载目录中的文件不影响资料库。删除订单或报销单按各自流程清理受管材料。

默认导出目录由 [ExportPreferences](../src/Eiri.Reimbursement.Desktop/ExportPreferences.cs) 保存到 `%LOCALAPPDATA%\EiriReimbursementHelper\export-settings.json`，不在数据库备份范围内。钉钉凭据、提审信息和审批记录位于数据库内，随整库备份恢复。

## 6. 核心流程

### 材料导入和识别

1. 用户指定订单与材料角色。发票接受 PDF；订单截图等辅助材料接受 PDF、PNG、JPG、JPEG，校验扩展名和文件签名。
2. 复制到 `staging` 后计算 SHA-256，检测重复内容，在数据库事务中登记文件，移动至最终路径后提交；异常路径执行补偿清理。
3. 发票交给 worker 分析；辅助材料只保存。报销单 PDF 另有首页字段提取流程，重复识别只补空字段。
4. 发票先解析文本层。任一页非空白字符少于 40 个、损坏字符超过 2%，或发票号、商家名称、金额、发票明细缺失或置信度低于 0.90 时，执行 300 DPI 本地 OCR。
5. 损坏字符包括替换符、控制符、私用区、代理及未分配字符。文本层和 OCR 分别解析；保留可靠文本字段及明细列顺序，补齐缺失字段和扫描续页。字段冲突、低置信度、未恢复的低质量页或 OCR 异常要求人工核对。可捕获的 OCR 异常保留已有文本结果；整个 worker 超时或崩溃由主应用处理为任务失败。
6. 文本层明细按“项目名称”“规格型号”“合计”的坐标裁取；无法定位表头或使用 OCR 时采用文本解析。候选与人工校正结果分开保存。

上述质量阈值是当前启发式规则，准确率仍需在更广泛样本上测量。

### 普通报销资料导出

[ReimbursementBatchExporter](../src/Eiri.Reimbursement.Infrastructure/Export/ReimbursementBatchExporter.cs) 读取所选订单及报销单关联订单，在目标位置创建“报销材料导出-总金额-yyyyMMdd-HHmmss”目录，同名追加序号。

- 汇总文件为带 UTF-8 BOM 的 CSV，列为“总金额、发票号”，每订单一行，末尾为合计。
- 发票输出逐页 PNG 和原始 PDF；辅助材料保留原件。
- 报销单导出要求存在 PDF 附件，每份附件首页转为 PNG。
- “打印材料”平铺保存报销单首页及辅助材料图片；辅助 PDF 转换全部页面。

当前普通导出直接写入新目录，失败时清理临时渲染目录，可能保留已生成的部分文件；尚未实现整批临时目录原子提交。桌面层在导出成功后更新已导出里程碑。文件生成后若状态保存失败，保留文件并报告错误。

### 审批 PDF

已同意报销单使用有效钉钉连接取得已提交表单数据。worker 生成 A4 首页，后续按订单顺序拼接辅助材料：PDF 保留原页，图片按比例完整置于横向或纵向 A4。首页放不下时失败，不缩小字号或截断内容。

服务先写临时 PDF，成功后移入目标位置；生成失败保留已有目标和里程碑。设置默认导出目录后直接使用该目录，未设置时单选选择文件、多选选择一次目录，每份报销单各生成一个 PDF。同名自动追加序号；成功后桌面层同步报销单及关联订单的已导出里程碑。

### 钉钉与归档

“选项 → 设置 → 钉钉”管理连接及提审信息。启动时，对已保存凭据且连接过期、令牌缺失或上次连接失败的情况尝试一次重连，成功后刷新待获取完整结果的未归档报销单。已拒绝和已撤销的未归档报销单可重新提审；结果不明时先核对，避免重复提交。

报销单的里程碑修改同步关联订单，成功导出报销单资料会同步已导出里程碑。订单和报销单完成全部里程碑后进入只读归档窗口，主窗口仅显示未归档条目；归档规则同样适用于既有资料和恢复后的资料。取消报销单归档会在同一事务中清空报销单及关联订单的已返款时间，保留已导出和已提交。删除报销单解绑订单并保留其里程碑。

### 备份恢复

备份包含 SQLite 一致快照、数据库引用的订单与报销单原始材料、元数据及哈希清单，不包含缓存、日志或外部导出文件。备份包不加密。

恢复先在资料库旁的临时目录解压、校验包格式、数据库和文件哈希，并执行适用迁移。验证成功后将旧目录暂存、把新目录移到原资料库路径；切换失败时尝试恢复旧目录。恢复替换整个当前资料库。

## 7. 验证边界

已具备订单与报销单管理、识别校正、归档、CSV 与图片导出、审批 PDF、整库备份恢复和 MSI 交付。用户于 2026-10-02 确认钉钉真实流程验收完成。低质量文本层 OCR 回退已在当前工作区实现，尚未重新打包为安装程序。

自动测试保留金额、材料去重、人工校正保护、数据库迁移、归档、导出成败、备份完整性、worker 协议及钉钉防重复提交等检查。WPF 测试聚焦窗口与绑定冒烟，主题和附件打开由人工验收。历史发布的测试数量与安装验证结果保留在各版本发布说明中。

worker 已有调用超时与进程终止逻辑，具体值见[worker 协议](../worker/document-worker/README.md)。文件大小、页数、渲染像素和进程内存配额尚未形成完整限制；当前进程适配器测试主要验证成功响应，故障恢复仍需补充验证。

构建与验证命令统一维护在[开发与交付指南](./development.md)。导出完整提交、构建路径保护、worker 故障与资源边界、依赖审计和下一次发布验收统一维护在[开发计划](./development-plan.md)。
