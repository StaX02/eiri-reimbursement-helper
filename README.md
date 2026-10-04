# Eiri Reimbursement Helper

面向清华大学直流研究中心报销流程的 Windows 本地发票报销助手，以订单和报销单整理材料、跟踪提交与返款进度。

应用支持发票 PDF 识别与人工校正、订单和报销单管理、报销里程碑与归档、钉钉提审及审批流程刷新、报销资料和审批 PDF 导出，以及整库备份恢复。导入材料复制到受管资料库，通过 SHA-256 去重并校验文件签名；发票先提取文本，必要时使用本地 OCR。

## 安装与使用

Windows 10/11 x64 用户可从 [GitHub Releases](https://github.com/StaX02/eiri-reimbursement-helper/releases) 获取 MSI 或免安装 ZIP。发布包包含 .NET、Python、PDF 和 OCR 依赖及模型。

MSI 支持选择安装目录、当前用户资料库目录和桌面快捷方式。默认资料库位于 `%LOCALAPPDATA%\EiriReimbursementHelper`；升级保留已有目录及资料，卸载默认保留资料。迁移资料库使用应用内的整库备份与恢复。

钉钉连接、提审信息和默认导出目录在“选项 → 设置”中管理。钉钉提审会上传图片和表单数据；连接凭据保存在本地数据库，随整库备份迁移。配置与故障处理见 [钉钉连接说明](docs/dingtalk-connection.md)。

本文描述当前源码。本地最新发布标签为 v0.5.0；其后新增内容见 [未发布变更](docs/release-notes/unreleased.md)，安装包内容与历史验证结果见 [v0.5.0 发布记录](docs/release-notes/v0.5.0.md)。

## 开发入口

开发环境为 Windows x64，.NET SDK 按 [global.json](global.json) 安装。开发模式运行文档识别、转图或相关测试前，需要准备 [Python worker](worker/document-worker/README.md)。

```powershell
dotnet restore Eiri.ReimbursementHelper.sln
dotnet build Eiri.ReimbursementHelper.sln --no-restore
dotnet test Eiri.ReimbursementHelper.sln --no-build
dotnet run --project src/Eiri.Reimbursement.Desktop/Eiri.Reimbursement.Desktop.csproj
```

环境准备、验证范围、打包命令、安装参数和发布检查见 [开发与交付指南](docs/development.md)。

## 项目文档

- [文档导航](docs/README.md)：按任务查找文档，说明各文档的维护职责。
- [领域词汇](CONTEXT.md)与[当前架构](docs/architecture.md)：理解业务概念、模块边界和现有行为。
- [开发计划](docs/development-plan.md)：下一阶段的优先级、依赖与验收条件。
- [界面规范](DESIGN.md)：视觉、交互和状态约定。
