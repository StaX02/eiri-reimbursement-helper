# Eiri Reimbursement Helper

Windows 本地发票报销助手。当前代码支持 WPF 应用启动、SQLite schema migration、订单创建与列表读取，以及在订单详情中选择或拖放发票 PDF 和订单截图。导入材料会复制到受管资料库，通过 SHA-256 去重，并校验文件签名。详情页支持人工校正发票字段、隔离进程提取 PDF 文本、对无文本层 PDF 执行本地 OCR，以及永久删除订单与其受管材料。

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

首次使用发票提取前初始化 worker：

```powershell
python -m venv worker/document-worker/.venv
worker/document-worker/.venv/Scripts/python.exe -m pip install -e worker/document-worker
```

开发前先阅读 [领域词汇](./CONTEXT.md) 和 [架构规划](./docs/architecture.md)。
