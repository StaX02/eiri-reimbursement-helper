# 发票报销助手架构规划

## 1. 目标与范围

首版是一款 Windows 单机桌面应用。用户手动创建订单，将文件明确导入为订单截图等辅助材料或原始发票 PDF；软件仅提取发票字段并允许人工校正，按订单展示汇总信息和可修改的报销里程碑，支持批量导出以及整库备份恢复。

首版容量目标为 10,000 个订单、50,000 个文件。数据保存在本地，不加密，不上传第三方服务。

首版暂缓：自动匹配文件与订单、云端 OCR、多人协作、移动端、应用内账号、导出历史。

## 2. 技术栈

| 区域 | 选择 | 用途 |
| --- | --- | --- |
| 运行时与语言 | .NET 10 LTS、C# | 主应用与业务逻辑 |
| 桌面界面 | WPF、CommunityToolkit.Mvvm | Windows 原生窗口、列表、拖放、预览和 MVVM |
| 结构化存储 | SQLite、Microsoft.Data.Sqlite、显式 SQL migration | 订单、发票、当前里程碑和提取结果 |
| 文件存储 | System.IO、受管资料库 | 原始材料、预览缓存和临时输出 |
| 文档工作进程 | 打包的 Python、pypdfium2、RapidOCR 3.9.2、ONNX Runtime 1.29.0、PP-OCRv6 small | PDF 文本抽取、页面渲染和本地中文 OCR |
| XLSX 与压缩包 | ClosedXML、System.IO.Compression | 报销汇总和备份包 |
| 合并 PDF | PDFsharp；样本不兼容时切换 qpdf adapter | 生成可选合并副本，原始 PDF 永不改写 |
| 测试 | xUnit、临时 SQLite、fixture/golden tests、Windows UI Automation smoke tests | 模块、集成、导出与关键界面验证 |
| 交付 | win-x64 self-contained MSIX | 无需预装 .NET，支持干净安装和卸载 |

选择依据见 [ADR-0004](./adr/0004-dotnet-wpf-desktop-stack.md) 和 [ADR-0005](./adr/0005-isolated-document-worker.md)。

## 3. 系统形状

```text
┌──────────────────────── WPF Desktop ────────────────────────┐
│ Order list / Detail editor / Export dialog / Backup dialog │
└───────────────────────────┬─────────────────────────────────┘
                            │ IReimbursementWorkspace
┌───────────────────────────▼─────────────────────────────────┐
│                    Reimbursement Core                       │
│ Orders │ Managed Library │ Export │ Backup │ Query policies │
└───────────────┬──────────────────────┬──────────────────────┘
                │                      │ IDocumentProcessor
        ┌───────▼────────┐      ┌──────▼──────────────────────┐
        │ SQLite + files │      │ isolated document-worker   │
        │ local adapters │      │ PDF text → OCR → candidates│
        └────────────────┘      └─────────────────────────────┘
```

WPF 只调用 `IReimbursementWorkspace`，不直接执行 SQL、复制原始材料或启动 OCR。主应用拥有数据库和受管资料库；文档工作进程只接收单次任务所需的文件路径和选项，并返回结构化候选字段。

## 4. 深模块与 interfaces

### Reimbursement Workspace

这是界面使用的主 interface，提供创建和查询订单、导入材料、校正发票、设置里程碑、导出、备份和恢复等用例。它隐藏跨数据库与文件系统的一致性处理、金额汇总、重复检测和后台任务编排。

建议 interface 形状：

```csharp
CreateOrder(CreateOrderCommand command)
ImportMaterials(OrderId orderId, IReadOnlyList<SourceFile> files)
UpdateInvoice(InvoiceId invoiceId, InvoiceCorrection correction)
SetMilestone(OrderId orderId, Milestone milestone, DateTimeOffset? occurredAt)
SearchOrders(OrderQuery query)
ExportOrders(IReadOnlyList<OrderId> orderIds, ExportOptions options)
CreateBackup(BackupTarget target)
RestoreBackup(BackupSource source)
```

### Document Processing

`IDocumentProcessor.Analyze(DocumentJob)` 是进程 seam。首版 adapter 使用 Python 工作进程；测试 adapter 返回固定结果；未来纯 .NET adapter 可以使用 PdfPig、PDFtoImage 和 RapidOCRLib。

协议采用 JSON Lines，通过 stdin/stdout 传输，包含 `protocolVersion`、`jobId`、文件类型、模型版本、超时以及结果状态。返回统一的 `TextBlock` 和 `FieldCandidate`，其中记录文本、页码、坐标、来源、置信度和 parser 版本。

### Managed Library

该模块负责导入、内容哈希、临时文件、原子改名、相对路径和启动时一致性修复。相同 SHA-256 文件禁止重复导入；相同发票号只提示，经用户确认后允许保存。

### Export

`ExportOrders` 隐藏订单快照、金额汇总、XLSX 生成、PDF 转图、原始 PDF 复制、可选 PDF 合并、清单校验和目标目录提交。它在目标目录旁创建临时目录，全部成功后再改名为最终目录，随后更新订单的 `exported_at`。应用不保存导出历史。

### Backup

`CreateBackup` 和 `RestoreBackup` 隐藏 SQLite 一致快照、原始材料清单、SHA-256 校验、ZIP 打包以及恢复前验证。预览缓存和日志可以重建，不进入备份包。

## 5. 数据模型

### orders

- `id`: UUID
- `platform`: `Taobao | JD | Other`
- `external_order_number`: 可空字符串
- `notes`: 可空字符串
- `exported_at`: 可空时间；成功导出自动设置，用户可修改或清空
- `submitted_at`: 可空时间；用户可修改或清空
- `refunded_at`: 可空时间；用户可修改或清空
- `created_at`, `updated_at`

里程碑不强制先后顺序。界面对“已返款但未提交”等组合给出提示，仍允许用户保存。

### managed_files

- `id`, `order_id`
- `role`: `OrderScreenshot | InvoicePdf`；`OrderScreenshot` 兼容保存订单截图和 PDF 等辅助材料
- `relative_path`, `media_type`, `byte_length`
- `sha256`: 全局唯一
- `processing_state`, `processing_error`
- `imported_at`

### invoices

- `id`, `order_id`, `managed_file_id`
- `merchant_name`
- `invoice_number`: 字符串，保留前导零；不设唯一约束
- `total_minor_units`: 有符号整数，以分为单位
- `currency`: 首版固定 `CNY`
- `needs_review`, `updated_at`
- `is_user_corrected`: 用户保存过人工校正后为 true；后续机器重分析只更新未人工校正的字段集合

### invoice_lines

- `id`, `invoice_id`, `sequence`
- `name`, `amount_minor_units`
- `is_effective`: 折扣、税额等非主要商品行设为 false

### extraction_results

每个文件只保存当前提取结果，不维护版本历史：`managed_file_id`、`worker_version`、`parser_version`、`candidates_json`、`completed_at` 和错误信息。机器候选与用户校正后的发票字段分开保存。

订单列表字段均为查询派生值：

- 商家名：发票商家名称去重合并
- 商品名：每张发票第一项有效明细，存在多项时显示“等 N 项”
- 金额：所有发票 `total_minor_units` 的有符号和
- 发票号：分别显示为标签
- 报销里程碑：由三个可空时间派生

## 6. 受管资料库

默认位置为 `%LocalAppData%/EiriReimbursementHelper`，用户可以迁移到其他目录。

```text
library.db
originals/
  orders/{order-id}/supporting-materials/{file-id}.{ext}
  orders/{order-id}/invoices/{file-id}.pdf
cache/
  previews/{sha256}/page-001.png
staging/
logs/
```

数据库只保存受管资料库内的相对路径。原始材料永不覆盖；预览、OCR 中间图片和派生汇总均可删除重建。

## 7. 核心处理流程

### 导入

1. 根据用户选择的投放区确定材料类型，再校验扩展名、媒体类型、文件大小和页数上限。
2. 流式计算 SHA-256，并复制到 `staging`。
3. 检测相同内容；重复文件停止导入。
4. 在数据库事务中登记材料，再原子移动到最终路径。
5. 发票提交分析任务；订单截图等辅助材料完成保存后不执行内容解析。分析失败只影响发票提取状态，原始材料仍可查看和人工录入。

### 发票提取

1. 使用 PDFium 文本层抽取带坐标文本。
2. 通过字符量、关键标签、发票号和金额格式执行质量门。
3. 没有文本层时以 300 DPI 渲染页面，再使用随 RapidOCR wheel 交付的 PP-OCRv6 small；低质量文本层回退将在后续质量门切片实现。
4. 票面 profile 优先根据“发票号码”“价税合计”“小写”“名称”等语义标签、金额格式和税号规则生成候选字段；坐标用于保留证据范围，并在文本语义不足时辅助区分销售方与购买方。
5. 缺失、冲突或低置信度字段标记 `needs_review`。
6. 用户校正写入 `invoices` 和 `invoice_lines`，原始候选保留在当前 `extraction_results` 中。

订单截图等辅助材料仅保存到受管资料库，不执行 OCR 或字段提取；订单与材料的归属由用户选择投放区确定。

### 导出

1. 对选中订单读取一致的数据快照并计算总金额。
2. 在目标目录旁生成临时输出：`报销汇总.xlsx`、逐页发票 PNG、原始 PDF、可选合并 PDF。
3. 校验清单、文件数量和金额后，将临时目录改为最终目录。
4. 更新订单 `exported_at`；用户之后仍可修改或清空。
5. 任一生成步骤失败时清理临时文件，订单状态不变。

目标目录提交成功后若 SQLite 状态更新失败，软件明确提示“文件已生成、状态未更新”，保留输出文件，并允许用户手动设置已导出。该处理避免删除用户已经得到的完整输出。

汇总工作簿至少包含“订单汇总”和“发票明细”两个工作表。

### 备份恢复

备份时生成 SQLite 一致快照，加入全部原始材料和 manifest，计算文件哈希后压缩。恢复时先解压到新目录、验证 schema 版本与全部哈希，成功后关闭当前资料库并切换目录。备份包不加密。

## 8. 可靠性与安全限制

- 文档工作进程不得获得整个资料库路径，只处理为任务准备的单个文件。
- 对文件大小、PDF 页数、图片像素、工作时间和并发数设置上限。
- 工作进程崩溃、超时或输出非法 JSON 时终止该任务，并允许重试或人工录入。
- SQLite 启用外键、WAL、busy timeout，并在启动时执行 schema migration 与资料库一致性检查。
- 金额只使用整数分，禁止浮点计算。
- 安装目录与资料库分离；升级程序不接触用户材料。
- 应用依赖 Windows 用户权限和 BitLocker，不实现应用密码或数据加密。

## 9. 测试策略

- Core：金额汇总、主要商品派生、里程碑修改和重复策略的 xUnit 测试。
- Persistence：临时 SQLite 与临时受管资料库集成测试，不用 repository mock 替代真实 SQL。
- Document worker：版本化 contract tests，以及脱敏真实样本的字段级回归集。
- Export：XLSX 内容、文件清单、失败回滚和重复导出的 golden tests。
- Backup：缺失文件、哈希损坏、schema 不兼容和完整恢复测试。
- UI：订单创建、拖放导入、人工校正、批量导出和里程碑修改的 UI Automation smoke tests。

## 10. 实施路线

1. **识别 PoC**：使用 20–30 份真实电子发票和淘宝、京东各 30–50 张脱敏截图，测量字段准确率、冷启动、单文件耗时和安装体积；同时验证 PDFsharp 合并兼容性及第三方许可清单。
2. **应用骨架**：建立 .NET solution、WPF shell、SQLite migration、受管资料库和测试工程。
3. **首条端到端切片**：创建订单 → 导入一份 PDF → 自动提取 → 人工校正 → 列表显示。
4. **完整订单能力**：多截图、多发票、发票明细、重复检测、检索筛选和三个里程碑。
5. **可靠导出**：多选、总金额、XLSX、发票 PNG、原始 PDF、可选合并 PDF及成功后更新状态。
6. **备份与交付**：整库备份恢复、故障恢复、资源限制、self-contained MSIX 和升级验证。

建议 PoC 质量门：发票号与金额字段达到 99% 准确率，商家名达到 98%，主要商品名达到 95%；任何未达到门槛的字段必须稳定进入人工校正流程。质量门基于项目自己的真实脱敏样本集评估。

## 11. 主要技术风险

- Python 文档工作进程会增加安装体积和冷启动时间，PoC 必须测量 PyInstaller/Nuitka 产物。
- 电商截图版式频繁变化，平台 profile 需要版本化，失败时保持文件可查看和字段可手填。
- PDF 字体、旋转和裁剪框可能影响文本坐标；同一 PDFium 后端同时承担文本和渲染可减少坐标偏差。
- 第三方原生库、模型和 PDFium 预编译文件需要固定版本，并随安装包提供 LICENSE/NOTICE 清单。
- 文件系统与 SQLite 无法共享单一事务，Managed Library 必须通过 staging、原子改名和启动修复集中处理一致性。

## 12. 参考依据

- [.NET release and support policy](https://learn.microsoft.com/en-us/dotnet/core/releases-and-support)
- [WPF repository and license](https://github.com/dotnet/wpf)
- [Microsoft.Data.Sqlite](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/)
- [MSIX overview](https://learn.microsoft.com/en-us/windows/msix/overview)
- [RapidOCR](https://github.com/RapidAI/RapidOCR)
- [pypdfium2](https://github.com/pypdfium2-team/pypdfium2)
- [RapidOCR bundled OCR models](https://github.com/RapidAI/RapidOCR)
- [ClosedXML](https://github.com/ClosedXML/ClosedXML)
- [PDFsharp](https://github.com/empira/PDFsharp)
