# 文档导航

从[项目 README](../README.md)了解产品及启动方式。开发前阅读[领域词汇](../CONTEXT.md)，再按任务选择下列文档。

## 按任务阅读

| 任务 | 文档 | 维护内容 |
| --- | --- | --- |
| 理解业务概念 | [CONTEXT.md](../CONTEXT.md) | 领域术语、关系及应避免的同义词 |
| 了解当前实现 | [架构](architecture.md) | 模块、数据模型、资料库和业务流程 |
| 配置环境、验证与发布 | [开发与交付](development.md) | 开发命令、测试边界、打包、安装和升级验证 |
| 选择下一项工作 | [开发计划](development-plan.md) | 现状证据、优先级、依赖、范围和验收条件 |
| 确定版本包含什么 | [未发布变更](release-notes/unreleased.md)、[v0.5.0](release-notes/v0.5.0.md)、[v0.4.0](release-notes/v0.4.0.md) | 当前增量与各次发布时的验证记录 |
| 修改界面 | [DESIGN.md](../DESIGN.md) | 视觉意图、共享样式和交互约定 |
| 接入或排查钉钉 | [钉钉连接](dingtalk-connection.md) | 凭据、模板、通讯录、提审、防重复提交和审批查询 |
| 修改识别或文档处理 | [worker README](../worker/document-worker/README.md) | 文档算法、协议、模型及独立运行方式 |
| 理解架构取舍 | 下方 ADR 列表 | 已作决策的背景、取舍及修订关系 |
| 使用工程技能 | [Issue tracker](agents/issue-tracker.md)、[triage labels](agents/triage-labels.md)、[领域文档规则](agents/domain.md) | GitHub 工单操作和文档消费约定 |

## 架构决策

- [ADR-0001：本地优先的 Windows 单机桌面架构](adr/0001-local-first-windows-desktop.md)
- [ADR-0002：导入材料复制到受管资料库](adr/0002-managed-file-library.md)
- [ADR-0003：整库备份与恢复](adr/0003-whole-library-backup.md)
- [ADR-0004：.NET 与 WPF](adr/0004-dotnet-wpf-desktop-stack.md)
- [ADR-0005：独立文档 worker](adr/0005-isolated-document-worker.md)
- [ADR-0006：WiX MSI 交付](adr/0006-wix-msi-delivery.md)，修订 ADR-0004 的早期 MSIX 方案。

## 维护约定

1. 术语定义维护在 `CONTEXT.md`；具体操作、失败处理和接口字段放到架构或对应专题文档。
2. 当前行为以源码及测试为依据。行为变化时更新对应文档，并在未发布变更中记录用户可感知的增量。
3. 计划写明依据、交付物、依赖与验收条件；任务认领和执行进度以 GitHub Issues 为准。远端读取失败时记录核对限制，恢复访问后先核对已有工单。
4. 发布记录对应固定版本，只记录当次实际完成的验证。当前源码、待验收目标与已发布安装包分别标明。
5. 新的难以逆转且存在实际取舍的决策使用 ADR；修订既有决策时注明被修订的 ADR。普通进度变化更新计划或 Issue。

修改后检查相对链接、命令参数和版本边界；移动内容时保留原有业务约束，并让入口指向新的权威位置。
