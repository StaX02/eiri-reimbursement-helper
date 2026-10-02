# Document worker

This directory contains the isolated Python process described by
[`docs/architecture.md`](../../docs/architecture.md). The .NET side of the versioned JSON Lines
protocol is implemented by `JsonLinesProcessDocumentProcessor`.

The worker extracts the native text layer with `pypdfium2`. Invoice analysis falls back to 300 DPI
RapidOCR 3.9.2 / ONNX Runtime 1.29.0 when any page has fewer than 40 non-whitespace characters,
more than 2% damaged characters (replacement, control, private-use, surrogate or unassigned),
or the native analysis lacks a confident invoice number, sales-merchant name, price-tax total or
product name. These are conservative heuristics, not measured accuracy guarantees.

Native and OCR text are parsed separately. Usable native fields and native product-column order
are preserved; OCR fills missing fields and scanned continuation pages. Conflicting scalar fields,
low-confidence OCR evidence, unresolved weak pages and OCR failures require manual review.
OCR failure preserves the partial native analysis. Both text sources remain in the worker response;
the .NET workspace continues to protect fields the user has already corrected.

The desktop publish target builds this worker with PyInstaller and copies the complete standalone
worker directory into the application. End users do not need Python or a virtual environment.

Bundled model SHA-256 values:

- `PP-OCRv6_det_small.onnx`: `090F04ABCD9D9A7498BC4EBF677E4CB9BDCE1FE4197DDB7E529F1EF44E1FF94F`
- `PP-OCRv6_rec_small.onnx`: `6F327246B50388F3C176AE304BD95767EA6DC0C9AE92153EF8CBE210B3C14884`
- `ch_ppocr_mobile_v2.0_cls_mobile.onnx`: `E47ACEDF663230F8863FF1AB0E64DD2D82B838FCEB5957146DAB185A89D6215C`

```powershell
python -m venv .venv
.venv\Scripts\python -m pip install -e .
.venv\Scripts\python -m unittest discover -s tests -v
```

To build only the standalone worker:

```powershell
.\build-worker.ps1 -OutputDirectory ..\..\artifacts\document-worker
```

Approved reimbursement export uses the `exportApprovedReimbursement` JSON Lines operation. ReportLab embeds the local Windows SimSun font for the A4 cover; pypdf appends original supporting PDF pages, and Pillow handles image orientation. The bundled worker includes these dependencies. A cover that cannot fit on one page at the requested font sizes fails without truncation.
