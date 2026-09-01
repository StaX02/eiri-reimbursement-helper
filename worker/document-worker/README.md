# Document worker

This directory contains the isolated Python process described by
[`docs/architecture.md`](../../docs/architecture.md). The .NET side of the versioned JSON Lines
protocol is implemented by `JsonLinesProcessDocumentProcessor`.

The current PoC extracts the native text layer with `pypdfium2` and returns invoice number,
sales-merchant name and price-tax total candidates for the checked-in electronic-invoice samples.
RapidOCR, ONNX Runtime and the pinned PP-OCRv5 mobile models will be added after the real-invoice
benchmark records compatible versions, model hashes, performance measurements and third-party
notices.

```powershell
python -m venv .venv
.venv\Scripts\python -m pip install -e .
.venv\Scripts\python -m unittest discover -s tests -v
```
