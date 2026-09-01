# Document worker

This directory contains the isolated Python process described by
[`docs/architecture.md`](../../docs/architecture.md). The .NET side of the versioned JSON Lines
protocol is implemented by `JsonLinesProcessDocumentProcessor`.

The worker extracts the native text layer with `pypdfium2`. When a PDF has no text layer, it renders
each page at 300 DPI and runs the models bundled with RapidOCR 3.9.2 through ONNX Runtime 1.29.0.
Both paths return invoice number, sales-merchant name and price-tax total candidates.

Bundled model SHA-256 values:

- `PP-OCRv6_det_small.onnx`: `090F04ABCD9D9A7498BC4EBF677E4CB9BDCE1FE4197DDB7E529F1EF44E1FF94F`
- `PP-OCRv6_rec_small.onnx`: `6F327246B50388F3C176AE304BD95767EA6DC0C9AE92153EF8CBE210B3C14884`
- `ch_ppocr_mobile_v2.0_cls_mobile.onnx`: `E47ACEDF663230F8863FF1AB0E64DD2D82B838FCEB5957146DAB185A89D6215C`

```powershell
python -m venv .venv
.venv\Scripts\python -m pip install -e .
.venv\Scripts\python -m unittest discover -s tests -v
```
