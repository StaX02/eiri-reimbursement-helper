# 在独立工作进程中解析 PDF 和执行 OCR

主应用通过版本化 JSON Lines 协议调用独立文档工作进程，首版工作进程使用打包的 Python、pypdfium2、RapidOCR、ONNX Runtime 和 PP-OCRv5 mobile。该选择增加安装体积和进程通信成本，但能使用成熟的中文 OCR 路径，并将损坏或恶意 PDF、原生库崩溃和高资源消耗隔离在主应用之外；未来可在同一 interface 后替换为纯 .NET adapter。

