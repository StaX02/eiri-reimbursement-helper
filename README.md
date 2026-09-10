# Eiri Reimbursement Helper

为了方便在清华大学直流研究中心完成报销流程，编写了这个本地发票报销助手。Vibe出来的东西还有不少问题，打算慢慢改了。

当前代码支持 WPF 应用启动、SQLite schema migration、订单创建与列表读取，以及在订单详情中选择或拖放发票 PDF 和订单截图。导入材料会复制到受管资料库，通过 SHA-256 去重，并校验文件签名。详情页支持人工校正发票字段、隔离进程提取 PDF 文本、对无文本层 PDF 执行本地 OCR，以及永久删除订单与其受管材料。

## Requirements

- .NET SDK 10.0.400 or a compatible 10.0 patch
- Windows 10/11 x64

## Build and test

```powershell
dotnet restore Eiri.ReimbursementHelper.sln
dotnet build Eiri.ReimbursementHelper.sln --no-restore
dotnet test Eiri.ReimbursementHelper.sln --no-build
```

.NET 测试保留关键检查点：金额换算、材料导入与去重、人工校正保护、订单与报销单状态同步、归档与恢复、导出成败、备份完整性、数据库迁移、worker 通信，以及钉钉请求校验、防重复提审和故障恢复。相同分支仅保留代表性输入；分页使用小型夹具。桌面测试只做窗口加载、选择状态和归档只读绑定的冒烟检查，不反复打开业务弹窗或自动生成截图。文案、颜色、尺寸、主题和附件打开通过人工验收。

## Run

```powershell
dotnet run --project src/Eiri.Reimbursement.Desktop/Eiri.Reimbursement.Desktop.csproj
```

开发模式会使用仓库内已有的 worker 虚拟环境。只在修改或测试 Python worker 时创建它：

```powershell
python -m venv worker/document-worker/.venv
worker/document-worker/.venv/Scripts/python.exe -m pip install -e worker/document-worker
```

## Publish

发布会自动把 Python 运行时、PDFium、OCR 依赖和模型打包到应用目录。最终用户无需安装 Python 或执行初始化命令：

```powershell
dotnet publish src/Eiri.Reimbursement.Desktop/Eiri.Reimbursement.Desktop.csproj -c Release -r win-x64 --self-contained true
```

完整产物位于 `src/Eiri.Reimbursement.Desktop/bin/Release/net10.0-windows/win-x64/publish`。

生成包含完整应用、开始菜单快捷方式和卸载注册信息的 MSI：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File installer/Build-Msi.ps1
```

MSI 产物位于 `artifacts/release/Eiri-Reimbursement-Helper-v<version>-win-x64.msi`。构建脚本从桌面项目读取版本，使用 WiX Toolset 6，并将 `icon.ico` 用作应用、快捷方式和“已安装的应用”图标。构建结束时会自动校验图标、内嵌 CAB、升级规则和完整 payload。

双击 MSI 后可选择应用安装位置、当前 Windows 用户的数据保存位置，以及是否添加桌面快捷方式。开始菜单快捷方式自动添加。应用默认安装在 Program Files，数据默认保存在 `%LOCALAPPDATA%\EiriReimbursementHelper`；选择其他磁盘时，数据保存在所选位置下的 `EiriReimbursementHelper` 专用文件夹。安装需要管理员权限，数据目录需允许当前用户写入。

升级和修复沿用已有目录，保留资料库。更换数据位置不会自动搬移原数据；迁移请先在应用内导出备份包，再在新资料库中恢复。数据位置按 Windows 用户保存，其他用户首次启动使用各自的默认资料库。

从 Windows“已安装的应用”卸载时，可选择保留或永久删除当前用户的应用数据，默认保留。删除范围包含数据库、原始材料、缓存、暂存和日志；外部导出、外部备份、其他用户的数据及资料库内无关文件会保留。清理失败会提示手动处理，并将详情写入 MSI 日志。

无人值守安装与卸载也支持显式参数（静默卸载默认保留数据）：

```powershell
msiexec /i Eiri-Reimbursement-Helper-v0.4.0-win-x64.msi /qn INSTALLFOLDER="D:\Apps\Eiri" DATADIRECTORY="D:\Documents\EiriReimbursementHelper" ADDDESKTOPSHORTCUT=1
msiexec /x Eiri-Reimbursement-Helper-v0.4.0-win-x64.msi /qn DELETEAPPDATA=1
```

安装动作使用 Windows 10/11 自带的 .NET Framework 4.8。发布前运行 `installer/tests/Verify-InstallerOptions.ps1` 和 `installer/tests/Test-MsiLifecycle.ps1`；后者仅允许在未安装本产品的环境运行，并使用临时测试数据。

开发前先阅读 [领域词汇](./CONTEXT.md) 和 [架构规划](./docs/architecture.md)。
