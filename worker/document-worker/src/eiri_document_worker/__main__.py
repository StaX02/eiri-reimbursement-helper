import json
import re
import sys
from decimal import Decimal
from pathlib import Path
from typing import Any

import pypdfium2 as pdfium

from eiri_document_worker import __version__


PROTOCOL_VERSION = 1
PARSER_VERSION = "pdfium-text-0.1"
INVOICE_NUMBER_PATTERN = re.compile(r"(?<!\d)\d{20}(?!\d)")
TAXPAYER_CODE_PATTERN = re.compile(r"^[0-9A-Z]{18}$")
PRICE_TAX_TOTAL_PATTERN = re.compile(
    r"^[零壹贰叁肆伍陆柒捌玖拾佰仟万亿圆元角分整]+\s*[¥￥]\s*(-?[\d,]+\.\d{2})$"
)


def analyze_pdf(file_path: Path) -> dict[str, Any]:
    if file_path.suffix.lower() != ".pdf":
        raise ValueError("The PDF text worker only accepts .pdf files.")
    if not file_path.is_file():
        raise FileNotFoundError(f"Document does not exist: {file_path}")

    text_blocks: list[dict[str, Any]] = []
    document = pdfium.PdfDocument(file_path)
    try:
        for page_index in range(len(document)):
            page = document[page_index]
            try:
                width, height = page.get_size()
                text_page = page.get_textpage()
                try:
                    text = text_page.get_text_range().strip()
                finally:
                    text_page.close()

                if text:
                    text_blocks.append(
                        {
                            "text": text,
                            "page": page_index + 1,
                            "bounds": {
                                "x": 0.0,
                                "y": 0.0,
                                "width": float(width),
                                "height": float(height),
                            },
                            "confidence": 1.0,
                            "source": "pdf-text",
                        }
                    )
            finally:
                page.close()
    finally:
        document.close()

    candidates: list[dict[str, Any]] = []
    for block in text_blocks:
        match = INVOICE_NUMBER_PATTERN.search(block["text"])
        if match:
            candidates.append(
                {
                    "field": "invoice_number",
                    "value": match.group(0),
                    "confidence": 1.0,
                    "source": "invoice-profile",
                    "page": block["page"],
                }
            )
            break

    for block in text_blocks:
        lines = [line.strip() for line in block["text"].splitlines() if line.strip()]
        taxpayer_code_indexes = [
            index for index, line in enumerate(lines) if TAXPAYER_CODE_PATTERN.fullmatch(line)
        ]
        if len(taxpayer_code_indexes) >= 2:
            seller_code_index = taxpayer_code_indexes[1]
            if seller_code_index > 0:
                candidates.append(
                    {
                        "field": "merchant_name",
                        "value": lines[seller_code_index - 1],
                        "confidence": 1.0,
                        "source": "invoice-profile",
                        "page": block["page"],
                    }
                )
                break

    for block in text_blocks:
        for line in (line.strip() for line in block["text"].splitlines()):
            match = PRICE_TAX_TOTAL_PATTERN.fullmatch(line)
            if not match:
                continue
            amount = Decimal(match.group(1).replace(",", ""))
            candidates.append(
                {
                    "field": "total_minor_units",
                    "value": str(int(amount * 100)),
                    "confidence": 1.0,
                    "source": "invoice-profile",
                    "page": block["page"],
                }
            )
            break
        else:
            continue
        break

    candidate_fields = {candidate["field"] for candidate in candidates}
    required_fields = {"merchant_name", "invoice_number", "total_minor_units"}

    return {
        "workerVersion": __version__,
        "parserVersion": PARSER_VERSION,
        "textBlocks": text_blocks,
        "candidates": candidates,
        "needsReview": not required_fields.issubset(candidate_fields),
    }


def handle_request(request: dict[str, Any]) -> dict[str, Any]:
    protocol_version = request.get("protocolVersion")
    if protocol_version != PROTOCOL_VERSION:
        raise ValueError(f"Unsupported protocol version: {protocol_version}")

    job = request.get("job")
    if not isinstance(job, dict):
        raise ValueError("Request does not contain a document job.")
    if job.get("kind") != 1:
        raise ValueError("The current PoC only supports invoice PDFs.")

    analysis = analyze_pdf(Path(job["filePath"]).resolve())
    return {"protocolVersion": PROTOCOL_VERSION, "analysis": analysis}


def main() -> int:
    request_line = sys.stdin.readline()
    if not request_line:
        sys.stderr.write("Document worker received no request.\n")
        return 2

    try:
        request = json.loads(request_line)
        response = handle_request(request)
    except Exception as exception:
        sys.stderr.write(f"{type(exception).__name__}: {exception}\n")
        return 1

    sys.stdout.write(json.dumps(response) + "\n")
    sys.stdout.flush()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
