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

开发前先阅读 [领域词汇](./CONTEXT.md) 和 [架构规划](./docs/architecture.md)。
