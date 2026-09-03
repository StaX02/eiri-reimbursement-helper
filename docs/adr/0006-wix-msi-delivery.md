# 使用 WiX MSI 交付 Windows 应用

应用保留 self-contained ZIP，同时提供 self-contained x64 MSI。MSI 使用 WiX Toolset 6 构建，将应用和内置文档 worker 完整嵌入安装包，以 `icon.ico` 作为应用、开始菜单快捷方式和“已安装的应用”图标。安装范围为全计算机，默认目录位于 64 位 Program Files；Windows Installer 负责维护、卸载和基于稳定 UpgradeCode 的大版本升级。此决策修订 ADR-0004 中的 MSIX 交付格式。
