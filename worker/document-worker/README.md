# Document worker

This directory contains the isolated Python process described by
[`docs/architecture.md`](../../docs/architecture.md). The .NET side of the versioned JSON Lines
protocol is implemented by `JsonLinesProcessDocumentProcessor`.

## Analysis

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

## Development and packaging

Run the following commands from `worker/document-worker` with Python 3.12 or newer.

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

## JSON Lines protocol, version 1

The implementation is defined by [`__main__.py`](src/eiri_document_worker/__main__.py),
[`DocumentContracts.cs`](../../src/Eiri.Reimbursement.Core/Documents/DocumentContracts.cs),
[`JsonLinesDocumentProtocol.cs`](../../src/Eiri.Reimbursement.Infrastructure/Documents/JsonLinesDocumentProtocol.cs)
and [`JsonLinesProcessDocumentProcessor.cs`](../../src/Eiri.Reimbursement.Infrastructure/Documents/JsonLinesProcessDocumentProcessor.cs).

Each process handles one request. The caller writes one UTF-8 JSON object followed by a newline
to stdin and closes stdin. The worker reads that first line, writes one response object followed
by a newline to stdout on success, and exits. There is no persistent session, progress stream,
request multiplexing or job-ID echo. The examples below use synthetic paths and values; callers
must supply existing input files and serialize each complete request onto one line.

### Analyze a PDF

Analysis requests omit `operation`. `kind` is numeric: `1` analyzes an invoice PDF across its pages;
`3` analyzes the first page of a reimbursement PDF. `DocumentKind.OrderScreenshot = 2` exists in
the .NET enum but is unsupported by the worker; supporting materials are stored without analysis.

```json
{"protocolVersion":1,"job":{"jobId":"00000000-0000-0000-0000-000000000001","filePath":"C:/example/invoice.pdf","kind":1,"timeout":"00:00:30"}}
```

The response has this shape; the empty arrays illustrate an analysis needing manual review:

```json
{"protocolVersion":1,"analysis":{"workerVersion":"0.1.0","parserVersion":"cn-einvoice-semantic-0.7","textBlocks":[],"candidates":[],"needsReview":true}}
```

`textBlocks` contain `text`, one-based `page`, `bounds` (`x`, `y`, `width`, `height`),
`confidence` and `source`. Candidates contain `field`, string `value`, `confidence`, `source`,
and optional `page` and `bounds`. Invoice fields are `merchant_name`, `invoice_number`,
`total_minor_units` and repeated `product_name` entries. Reimbursement fields are
`application_date`, `reimbursement_type`, `reimbursement_content` and `total_minor_units`;
their parser version is `cn-reimbursement-semantic-1.0`. Amount values are signed integer
minor units represented as strings. `needsReview` requests manual checking; it does not
indicate a process failure.

### Render PDF pages

`render` writes 300 DPI PNG files named `page-1.png`, `page-2.png`, and so on. Set
`firstPageOnly` to `true` for the first page; omission or `false` renders all pages.
Use a fresh output directory because these filenames can overwrite files already there.

```json
{"protocolVersion":1,"operation":"render","job":{"jobId":"00000000-0000-0000-0000-000000000002","filePath":"C:/example/invoice.pdf","outputDirectory":"C:/example/rendered","firstPageOnly":false}}
```

```json
{"protocolVersion":1,"renderedFiles":["C:/example/rendered/page-1.png"]}
```

The worker returns absolute paths. The .NET caller verifies that each returned file exists
inside the requested output directory. Empty PDF output may be an empty list; the export
services enforce the page counts required by their workflows.

### Generate an approval PDF

The desktop app obtains the submitted form data from DingTalk before calling this operation.
The worker receives plain field names and values; it does not fetch DingTalk data.

```json
{"protocolVersion":1,"operation":"exportApprovedReimbursement","job":{"destinationPath":"C:/example/approval.pdf","reimburser":"Example User","fields":[{"name":"Example field","value":"Example value"}],"supportingMaterialPaths":["C:/example/supporting.pdf"],"fontPath":"C:/Windows/Fonts/simsun.ttc"}}
```

```json
{"protocolVersion":1,"written":true}
```

`supportingMaterialPaths` is ordered and accepts PDF, PNG, JPG and JPEG. The worker appends
PDF pages and places each image proportionally on a portrait or landscape A4 page.
The .NET caller supplies the system Fonts directory's `simsun.ttc`, checks the response
version and `written`, and verifies the destination exists. The worker writes directly to
its supplied destination; `ApprovedReimbursementExporter` supplies a temporary path and
commits the finished file to the user's target.

### Timeouts, failures and limits

| Operation | Current .NET timeout |
| --- | --- |
| Invoice analysis | 30 seconds, supplied by `SqliteReimbursementWorkspace` |
| Reimbursement analysis | 90 seconds, supplied by `SqliteReimbursementWorkspace` |
| PDF rendering | 2 minutes |
| Approval PDF generation | 5 minutes |

`AnalyzeAsync` accepts a positive `DocumentJob.Timeout`; the workspace supplies the values
above. The Python worker does not enforce the serialized `timeout` value. The .NET caller
applies a linked cancellation deadline while exchanging data and waiting for process exit,
terminates the process tree on timeout or cancellation, and reports timeout as `TimeoutException`.
Caller cancellation remains cancellation.

A missing request exits with code `2`; other caught errors exit with code `1` and write the
exception type and message to stderr. Failed requests have no JSON error envelope. The .NET
caller rejects nonzero exit codes, empty responses, malformed JSON and mismatched protocol
versions. Recoverable invoice OCR exceptions retain the native analysis with `needsReview=true`;
a process crash or deadline expiry cannot return that partial result. Reimbursement OCR errors
fail the request.

The protocol currently provides no file-size, page-count, rendered-pixel, process-memory or
message-size quota. The operation dispatch recognizes `render` and `exportApprovedReimbursement`;
other values currently fall through to analysis by `kind`. Callers should use only the documented
requests. Worker output creation is not transactional and may leave partial files after failure;
the owning export service is responsible for cleanup and committing complete output.
