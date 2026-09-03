# 使用 .NET 10 LTS 和 WPF 构建 Windows 桌面应用

应用采用 .NET 10 LTS、C# 和 WPF，使用 SQLite 保存结构化数据。项目已明确只支持 Windows 单机使用，因此 WPF 的平台限定不会损失目标能力；该组合能直接处理本地文件、进程和 Windows 安装，避免引入 WebView、前后端 IPC 以及双套界面构建链。交付格式由 ADR-0006 修订。

