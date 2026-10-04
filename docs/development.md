# 开发与交付指南

本文说明当前源码的开发、验证和 Windows 交付流程。开始修改前阅读[领域词汇](../CONTEXT.md)和[当前架构](./architecture.md)；已发布版本的范围以[发布记录](./release-notes/)为准。以下命令均在仓库根目录的 PowerShell 中执行。

## 开发环境

- Windows 10/11 x64。
- .NET SDK 由 [`global.json`](../global.json) 固定为 `10.0.400`，`latestPatch` 仅允许同一 `10.0.4xx` feature band 的补丁版本。
- Python 3.12 或更高版本，用于开发文档 worker 和构建发布包；依赖以 [`pyproject.toml`](../worker/document-worker/pyproject.toml) 为准。
- MSI 使用项目引用的 WiX Toolset 6；安装自定义动作运行于 Windows 自带的 .NET Framework 4.8。

检查本机工具：

```powershell
dotnet --version
python --version
```

桌面应用版本从项目文件读取；源码版本号与已发布安装包分别核对：

```powershell
$desktopProject = 'src/Eiri.Reimbursement.Desktop/Eiri.Reimbursement.Desktop.csproj'
$version = (Select-Xml -Path $desktopProject -XPath '/Project/PropertyGroup/Version' | Select-Object -First 1).Node.InnerText
$version
```

## 本地运行与测试

开发文档识别、转图或运行相关测试前，创建仓库内的 worker 虚拟环境：

```powershell
python -m venv worker/document-worker/.venv
worker/document-worker/.venv/Scripts/python.exe -m pip install -e worker/document-worker
worker/document-worker/.venv/Scripts/python.exe -m unittest discover -s worker/document-worker/tests -v
```

桌面应用会优先使用已配置的 worker，其次使用应用目录内的打包 worker，开发时可自动发现上述 `.venv`。worker 的隔离边界见 [ADR-0005](./adr/0005-isolated-document-worker.md)。

```powershell
dotnet restore Eiri.ReimbursementHelper.sln
dotnet build Eiri.ReimbursementHelper.sln --no-restore
dotnet test Eiri.ReimbursementHelper.sln --no-build
dotnet run --project src/Eiri.Reimbursement.Desktop/Eiri.Reimbursement.Desktop.csproj
```

.NET 测试覆盖金额换算、材料导入与去重、人工校正保护、订单与报销单同步、归档与恢复、导出成败、备份完整性、数据库迁移、worker 通信，以及钉钉请求校验、防重复提审和故障恢复。同一分支保留代表性输入，分页使用小型夹具。

桌面自动化测试检查窗口加载、选择状态和归档只读绑定；文案、颜色、尺寸、主题、附件打开及完整业务操作通过人工验收。验证记录应注明实际执行的命令、源码版本、结果及未覆盖项。

## 发布目录与安装包

发布会打包 Python 运行时、PDFium、OCR 依赖和模型，最终用户无需安装 Python。构建过程通过 [`build-worker.ps1`](../worker/document-worker/build-worker.ps1) 创建独立打包环境，每次构建均执行依赖安装，需能访问依赖源。

单独生成 self-contained 应用目录：

```powershell
dotnet publish src/Eiri.Reimbursement.Desktop/Eiri.Reimbursement.Desktop.csproj -c Release -r win-x64 --self-contained true
```

该命令的完整产物位于 `src/Eiri.Reimbursement.Desktop/bin/Release/net10.0-windows/win-x64/publish/`。

生成 MSI：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File installer/Build-Msi.ps1
```

[`Build-Msi.ps1`](../installer/Build-Msi.ps1) 默认读取桌面项目版本，先发布到 `artifacts/release/v<version>/win-x64/`，再生成 `artifacts/release/Eiri-Reimbursement-Helper-v<version>-win-x64.msi`。`-Version` 可显式指定版本；`-SkipPublish` 仅用于复用该版本目录中已核对的完整产物。

脚本自动检查应用图标、MSI 版本、快捷方式、内嵌 CAB、升级规则、安装选项和数据清理边界，并对比发布文件与 MSI 文件名及大小（排除 `.pdb`）。完成后生成同名 `.msi.sha256` 文件。安装选项验证还会在 `artifacts/installer-options/` 留下界面图像，供人工检查。

MSI 构建脚本为发布和安装项目构建设置了 `NuGetAudit=false`。发布前需独立联网审计 NuGet 依赖，并处理审计结果：

```powershell
dotnet restore Eiri.ReimbursementHelper.sln --force --no-http-cache -p:NuGetAudit=true -p:NuGetAuditMode=all
dotnet list Eiri.ReimbursementHelper.sln package --vulnerable --include-transitive
dotnet restore installer/CustomActions/CustomActions.csproj --force --no-http-cache -p:NuGetAudit=true -p:NuGetAuditMode=all
dotnet list installer/CustomActions/CustomActions.csproj package --vulnerable --include-transitive
```

ZIP 需独立生成。下面使用 MSI 构建时的发布目录；先运行上文的版本读取命令，确认目录与本次版本对应：

```powershell
$publishDirectory = "artifacts/release/v$version/win-x64"
$zipPath = "artifacts/release/Eiri-Reimbursement-Helper-v$version-win-x64.zip"
Compress-Archive -Path "$publishDirectory/*" -DestinationPath $zipPath
Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
```

交付 ZIP 前，解压到新目录，对照发布目录核对文件清单与 SHA-256，确认桌面程序和 `document-worker/eiri-document-worker.exe` 均在内，并实际启动、识别和导出。MSI 自动校验不覆盖 ZIP；发布记录分别保存两种产物的校验和及验证结果。

## 安装与升级验证

MSI 构建已执行静态安装选项检查，也可在构建后单独运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File installer/tests/Verify-InstallerOptions.ps1
```

[`Test-MsiLifecycle.ps1`](../installer/tests/Test-MsiLifecycle.ps1) 会实际安装、修复和卸载产品，并测试数据保留、重新选择资料库和显式清理。仅在未安装本产品的隔离 Windows 环境运行，使用临时测试数据；脚本需要管理员权限。

```powershell
$msiPath = "artifacts/release/Eiri-Reimbursement-Helper-v$version-win-x64.msi"
powershell -NoProfile -ExecutionPolicy Bypass -File installer/tests/Test-MsiLifecycle.ps1 -MsiPath $msiPath
```

验证跨版本升级时，`-MsiPath` 指向旧版本，另传可选参数 `-UpgradeMsiPath` 指向本次版本：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File installer/tests/Test-MsiLifecycle.ps1 -MsiPath 'C:/TestPackages/previous.msi' -UpgradeMsiPath $msiPath
```

验收时保留生命周期日志及人工操作结果，检查安装、修复、升级、默认保留数据卸载和显式删除数据卸载。交付设计见 [ADR-0004](./adr/0004-dotnet-wpf-desktop-stack.md) 与 [ADR-0006](./adr/0006-wix-msi-delivery.md)。

## 数据位置、迁移与卸载

双击 MSI 可选择安装位置、当前 Windows 用户的数据保存位置和桌面快捷方式。应用默认安装到 Program Files，开始菜单快捷方式自动添加；安装需要管理员权限。

数据默认位于 `%LOCALAPPDATA%\EiriReimbursementHelper`。选择其他位置时使用末级名为 `EiriReimbursementHelper` 的专用文件夹，当前用户需具有写入权限。资料库位置按 Windows 用户保存，其他用户首次启动使用各自的默认资料库。

升级和修复沿用已有目录并保留资料库。更换数据位置时，先在应用内导出备份包，再在新资料库恢复；安装程序不自动搬移原数据。恢复后核对订单、报销单、原始材料和里程碑，再处理旧资料库。

从 Windows“已安装的应用”卸载时，默认保留资料库，也可明确选择永久删除当前用户的数据。清理范围包含数据库、原始材料、缓存、暂存和日志；外部导出、外部备份、其他用户数据及资料库内无关文件保留。清理失败会提示手动处理，详情写入 MSI 日志。

无人值守操作通过 MSI 公开属性设置目录和快捷方式。以下示例使用前文定义的 `$msiPath`；卸载示例中的 `DELETEAPPDATA=1` 会永久删除当前用户的受管数据，省略该属性则保留：

```powershell
msiexec /i $msiPath /qn INSTALLFOLDER="D:\Apps\Eiri" DATADIRECTORY="D:\Documents\EiriReimbursementHelper" ADDDESKTOPSHORTCUT=1
msiexec /x $msiPath /qn DELETEAPPDATA=1
```
