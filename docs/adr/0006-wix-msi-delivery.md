# 使用 WiX MSI 交付 Windows 应用

应用保留 self-contained ZIP，同时提供 self-contained x64 MSI。MSI 使用 WiX Toolset 6 构建，将应用和内置文档 worker 完整嵌入安装包，以 `icon.ico` 作为应用、开始菜单快捷方式和“已安装的应用”图标。安装范围为全计算机，默认目录位于 64 位 Program Files；Windows Installer 负责维护、卸载和基于稳定 UpgradeCode 的大版本升级。此决策修订 ADR-0004 中的 MSIX 交付格式。

v0.4.0 增加中文安装选项：可选择独立的安装目录与当前用户的资料库目录，开始菜单快捷方式必装，桌面快捷方式可选。资料库目录以 `EiriReimbursementHelper` 为末级名称，避免把普通资料目录当成应用专用根目录。路径保存在 HKCU，卸载默认保留，以便重新安装后恢复使用；应用仍为每个 Windows 用户维护独立资料库。安装目录与桌面快捷方式偏好保存在 HKLM。

WiX DTF 的 x64 .NET Framework 4.8 自定义动作承载安装选项和卸载确认，在完整界面与基本界面模式显示。静默模式通过公开 MSI 属性配置。升级和修复锁定已有目录，升级中的旧版本卸载禁止清理数据。用户明确选择删除数据后，以当前用户身份在卸载提交阶段清理已登记的受管内容；不接受卸载命令提供的任意清理路径，不遍历符号链接或目录联接，保留无关文件。清理失败记录日志并显示警告，避免在部分删除后回滚 MSI。

依据：[WiX DTF 自定义动作](https://docs.firegiant.com/wix/tools/dtf/)、[Windows Installer 提交动作](https://learn.microsoft.com/en-us/windows/win32/msi/commit-custom-actions)。
