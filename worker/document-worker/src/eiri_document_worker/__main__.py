import json
import re
import sys
from decimal import Decimal
from pathlib import Path
from typing import Any

import pypdfium2 as pdfium

from eiri_document_worker import __version__


PROTOCOL_VERSION = 1
PARSER_VERSION = "cn-einvoice-semantic-0.5"
INVOICE_NUMBER_PATTERN = re.compile(r"(?<!\d)\d{20}(?!\d)")
LABELED_INVOICE_NUMBER_PATTERN = re.compile(
    r"发\s*票\s*号\s*码\s*[:：]?\s*(\d{20})(?!\d)"
)
TAXPAYER_CODE_PATTERN = re.compile(r"^[0-9A-Z]{18}$")
PRICE_TAX_TOTAL_PATTERN = re.compile(
    r"[零壹贰叁肆伍陆柒捌玖拾佰仟万亿圆元角分整]+"
    r"\s*(?:[（(]\s*小\s*写\s*[)）]\s*)?[¥￥]\s*(-?[\d,]+\.\d{2})"
)
SMALL_PRICE_TAX_TOTAL_PATTERN = re.compile(
    r"[（(]?\s*小\s*写\s*[)）]?\s*[:：]?\s*[¥￥]\s*(-?[\d,]+\.\d{2})"
)
MERCHANT_NAME_PATTERN = re.compile(r"^名称\s*[:：]\s*(.+)$")
PROJECT_MARKER_PATTERN = re.compile(r"[*＊][^*＊\r\n]+[*＊]")
QUANTITY_ROW_PATTERN = re.compile(
    r"(?:个|片|套|台|件|项|次|批|张|只|盒|包|组|米|千克|公斤)\s+\d+(?:\.\d+)?\s"
)
OCR_RENDER_SCALE = 300 / 72


def build_semantic_pages(text_blocks: list[dict[str, Any]]) -> list[dict[str, Any]]:
    pages: list[dict[str, Any]] = []
    for page_number in sorted({int(block["page"]) for block in text_blocks}):
        blocks = [block for block in text_blocks if block["page"] == page_number]
        blocks.sort(
            key=lambda block: (
                block["bounds"]["y"],
                block["bounds"]["x"],
            )
        )
        left = min(block["bounds"]["x"] for block in blocks)
        top = min(block["bounds"]["y"] for block in blocks)
        right = max(
            block["bounds"]["x"] + block["bounds"]["width"] for block in blocks
        )
        bottom = max(
            block["bounds"]["y"] + block["bounds"]["height"] for block in blocks
        )
        pages.append(
            {
                "text": "\n".join(block["text"] for block in blocks),
                "page": page_number,
                "bounds": {
                    "x": left,
                    "y": top,
                    "width": right - left,
                    "height": bottom - top,
                },
                "source": (
                    "ocr" if any(block["source"] == "ocr" for block in blocks) else "pdf-text"
                ),
            }
        )
    return pages


def find_total_amount(text: str) -> Decimal | None:
    for pattern in (PRICE_TAX_TOTAL_PATTERN, SMALL_PRICE_TAX_TOTAL_PATTERN):
        match = pattern.search(text)
        if match:
            return Decimal(match.group(1).replace(",", ""))
    return None


def find_product_names(text: str) -> list[str]:
    table_text = text.split("项目名称", 1)[-1]
    matches = list(PROJECT_MARKER_PATTERN.finditer(table_text))
    product_names: list[str] = []
    for index, match in enumerate(matches):
        segment_end = matches[index + 1].start() if index + 1 < len(matches) else len(table_text)
        segment = table_text[match.start():segment_end]
        segment = re.split(r"\r?\n\s*合\s*计|\r?\n\s*价税合计", segment, maxsplit=1)[0]
        lines = [line.strip() for line in segment.splitlines() if line.strip()]
        if not lines:
            continue

        marker = match.group(0).replace("＊", "*")
        fragments = [marker]
        first_line_remainder = lines[0][len(match.group(0)):].strip()
        candidate_lines = [first_line_remainder, *lines[1:]]
        for line_index, line in enumerate(candidate_lines):
            if not line:
                continue
            if line.startswith(("(", "（")):
                break
            quantity_match = QUANTITY_ROW_PATTERN.search(line)
            if quantity_match:
                if line_index == 0:
                    inline_product = line[:quantity_match.start()].split(maxsplit=1)[0]
                    if inline_product:
                        fragments.append(inline_product)
                    continue
                break
            first_fragment = line.split(maxsplit=1)[0]
            if first_fragment:
                fragments.append(first_fragment)

        product_name = "".join(fragments)
        if product_name != marker:
            product_names.append(product_name)

    return product_names


def extract_ocr_text_blocks(file_path: Path) -> tuple[list[dict[str, Any]], dict[int, tuple[float, float]]]:
    from rapidocr import RapidOCR

    engine = RapidOCR()
    text_blocks: list[dict[str, Any]] = []
    page_sizes: dict[int, tuple[float, float]] = {}
    document = pdfium.PdfDocument(file_path)
    try:
        for page_index in range(len(document)):
            page = document[page_index]
            try:
                width, height = page.get_size()
                page_number = page_index + 1
                page_sizes[page_number] = (float(width), float(height))
                image = page.render(scale=OCR_RENDER_SCALE).to_numpy()
                result = engine(image)
                if result.txts is None or result.boxes is None or result.scores is None:
                    continue
                for text, score, box in zip(result.txts, result.scores, result.boxes):
                    if not text.strip():
                        continue
                    xs = [float(point[0]) / OCR_RENDER_SCALE for point in box]
                    ys = [float(point[1]) / OCR_RENDER_SCALE for point in box]
                    text_blocks.append(
                        {
                            "text": text.strip(),
                            "page": page_number,
                            "bounds": {
                                "x": min(xs),
                                "y": min(ys),
                                "width": max(xs) - min(xs),
                                "height": max(ys) - min(ys),
                            },
                            "confidence": float(score),
                            "source": "ocr",
                        }
                    )
            finally:
                page.close()
    finally:
        document.close()
    return text_blocks, page_sizes


def build_ocr_seller_regions(
    text_blocks: list[dict[str, Any]],
    page_sizes: dict[int, tuple[float, float]],
) -> list[dict[str, Any]]:
    regions: list[dict[str, Any]] = []
    for page_number, (width, height) in page_sizes.items():
        right_side_blocks = [
            block
            for block in text_blocks
            if block["page"] == page_number
            and block["bounds"]["x"] + block["bounds"]["width"] / 2 >= width / 2
        ]
        right_side_blocks.sort(key=lambda block: (block["bounds"]["y"], block["bounds"]["x"]))
        text = "\n".join(block["text"] for block in right_side_blocks)
        if "销售方信息" in re.sub(r"\s+", "", text):
            regions.append(
                {
                    "text": text,
                    "page": page_number,
                    "bounds": {
                        "x": width / 2,
                        "y": 0.0,
                        "width": width / 2,
                        "height": height,
                    },
                }
            )
    return regions


def analyze_pdf(file_path: Path) -> dict[str, Any]:
    if file_path.suffix.lower() != ".pdf":
        raise ValueError("The PDF text worker only accepts .pdf files.")
    if not file_path.is_file():
        raise FileNotFoundError(f"Document does not exist: {file_path}")

    text_blocks: list[dict[str, Any]] = []
    seller_regions: list[dict[str, Any]] = []
    document = pdfium.PdfDocument(file_path)
    try:
        for page_index in range(len(document)):
            page = document[page_index]
            try:
                width, height = page.get_size()
                text_page = page.get_textpage()
                try:
                    text = text_page.get_text_range().strip()
                    seller_text = text_page.get_text_bounded(
                        left=width / 2,
                        bottom=0,
                        right=width,
                        top=height,
                    ).strip()
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
                if "销售方信息" in re.sub(r"\s+", "", seller_text):
                    seller_regions.append(
                        {
                            "text": seller_text,
                            "page": page_index + 1,
                            "bounds": {
                                "x": float(width / 2),
                                "y": 0.0,
                                "width": float(width / 2),
                                "height": float(height),
                            },
                        }
                    )
            finally:
                page.close()
    finally:
        document.close()

    if not text_blocks:
        text_blocks, page_sizes = extract_ocr_text_blocks(file_path)
        seller_regions = build_ocr_seller_regions(text_blocks, page_sizes)

    semantic_pages = build_semantic_pages(text_blocks)
    candidates: list[dict[str, Any]] = []
    for page in semantic_pages:
        match = LABELED_INVOICE_NUMBER_PATTERN.search(page["text"])
        if not match:
            match = INVOICE_NUMBER_PATTERN.search(page["text"])
        if match:
            candidates.append(
                {
                    "field": "invoice_number",
                    "value": match.group(1) if match.lastindex else match.group(0),
                    "confidence": 0.99 if match.lastindex else 0.98,
                    "source": "invoice-profile",
                    "page": page["page"],
                    "bounds": page["bounds"],
                }
            )
            break

    for region in seller_regions:
        lines = [line.strip() for line in region["text"].splitlines() if line.strip()]
        merchant_match = next(
            (match for line in lines if (match := MERCHANT_NAME_PATTERN.fullmatch(line))),
            None,
        )
        if merchant_match:
            candidates.append(
                {
                    "field": "merchant_name",
                    "value": merchant_match.group(1).strip(),
                    "confidence": 0.95,
                    "source": "invoice-profile",
                    "page": region["page"],
                    "bounds": region["bounds"],
                }
            )
            break
        taxpayer_code_indexes = [
            index for index, line in enumerate(lines) if TAXPAYER_CODE_PATTERN.fullmatch(line)
        ]
        if taxpayer_code_indexes:
            seller_code_index = taxpayer_code_indexes[0]
            if seller_code_index > 0:
                candidates.append(
                    {
                        "field": "merchant_name",
                        "value": lines[seller_code_index - 1],
                        "confidence": 0.99,
                        "source": "invoice-profile",
                        "page": region["page"],
                        "bounds": region["bounds"],
                    }
                )
                break

    if not any(candidate["field"] == "merchant_name" for candidate in candidates):
        for page in semantic_pages:
            lines = [line.strip() for line in page["text"].splitlines() if line.strip()]
            merchant_match = next(
                (match for line in lines if (match := MERCHANT_NAME_PATTERN.fullmatch(line))),
                None,
            )
            if merchant_match:
                candidates.append(
                    {
                        "field": "merchant_name",
                        "value": merchant_match.group(1).strip(),
                        "confidence": 0.90,
                        "source": "invoice-profile",
                        "page": page["page"],
                        "bounds": page["bounds"],
                    }
                )
                break

    for page in semantic_pages:
        amount = find_total_amount(page["text"])
        if amount is not None:
            candidates.append(
                {
                    "field": "total_minor_units",
                    "value": str(int(amount * 100)),
                    "confidence": 0.95 if page["source"] == "ocr" else 0.99,
                    "source": "invoice-profile",
                    "page": page["page"],
                    "bounds": page["bounds"],
                }
            )
            break

    for page in semantic_pages:
        for product_name in find_product_names(page["text"]):
            candidates.append(
                {
                    "field": "product_name",
                    "value": product_name,
                    "confidence": 0.90 if page["source"] == "ocr" else 0.98,
                    "source": "invoice-profile",
                    "page": page["page"],
                    "bounds": page["bounds"],
                }
            )

    required_fields = {"merchant_name", "invoice_number", "total_minor_units", "product_name"}
    candidates_by_field = {candidate["field"]: candidate for candidate in candidates}
    complete_high_confidence_result = all(
        field in candidates_by_field and candidates_by_field[field]["confidence"] >= 0.90
        for field in required_fields
    )

    return {
        "workerVersion": __version__,
        "parserVersion": PARSER_VERSION,
        "textBlocks": text_blocks,
        "candidates": candidates,
        "needsReview": not complete_high_confidence_result,
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
