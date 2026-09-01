import json
import subprocess
import sys
import tempfile
import unittest
import uuid
from decimal import Decimal
from pathlib import Path

import pypdfium2 as pdfium
from eiri_document_worker.__main__ import find_total_amount


WORKER_ROOT = Path(__file__).resolve().parents[1]
WORKER_SCRIPT = WORKER_ROOT / "src" / "eiri_document_worker" / "__main__.py"
INVOICE_EXAMPLES = Path(__file__).resolve().parents[3] / "examples" / "invoice"


def build_text_pdf(text: str) -> bytes:
    content = f"BT /F1 18 Tf 72 720 Td ({text}) Tj ET".encode("ascii")
    objects = [
        b"<< /Type /Catalog /Pages 2 0 R >>",
        b"<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        (
            b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] "
            b"/Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>"
        ),
        b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        b"<< /Length " + str(len(content)).encode("ascii") + b" >>\nstream\n" + content + b"\nendstream",
    ]

    pdf = bytearray(b"%PDF-1.4\n")
    offsets = [0]
    for number, body in enumerate(objects, start=1):
        offsets.append(len(pdf))
        pdf.extend(f"{number} 0 obj\n".encode("ascii"))
        pdf.extend(body)
        pdf.extend(b"\nendobj\n")

    xref_offset = len(pdf)
    pdf.extend(f"xref\n0 {len(objects) + 1}\n".encode("ascii"))
    pdf.extend(b"0000000000 65535 f \n")
    for offset in offsets[1:]:
        pdf.extend(f"{offset:010d} 00000 n \n".encode("ascii"))
    pdf.extend(
        (
            f"trailer\n<< /Size {len(objects) + 1} /Root 1 0 R >>\n"
            f"startxref\n{xref_offset}\n%%EOF\n"
        ).encode("ascii")
    )
    return bytes(pdf)


def analyze_document(pdf_path: Path) -> dict:
    request = {
        "protocolVersion": 1,
        "job": {
            "jobId": str(uuid.uuid4()),
            "filePath": str(pdf_path),
            "kind": 1,
            "timeout": "00:00:10",
        },
    }
    completed = subprocess.run(
        [sys.executable, str(WORKER_SCRIPT)],
        input=json.dumps(request) + "\n",
        capture_output=True,
        text=True,
        check=False,
        timeout=10,
    )
    if completed.returncode != 0:
        raise AssertionError(completed.stderr)
    return json.loads(completed.stdout)["analysis"]


def create_image_only_pdf(source_path: Path, destination_path: Path) -> None:
    document = pdfium.PdfDocument(source_path)
    try:
        page = document[0]
        try:
            image = page.render(scale=300 / 72).to_pil().convert("RGB")
            image.save(destination_path, "PDF", resolution=300)
        finally:
            page.close()
    finally:
        document.close()


class WorkerProtocolTests(unittest.TestCase):
    def test_total_amount_parser_accepts_common_semantic_layouts(self) -> None:
        examples = {
            "价税合计(大写) 壹仟贰佰叁拾肆圆伍角陆分 (小写) ¥1,234.56": Decimal("1234.56"),
            "（小写）： ￥ 98.00": Decimal("98.00"),
            "价税合计（大写）\n负贰拾圆整\n(小写) ¥-20.00": Decimal("-20.00"),
            "陆佰捌拾圆整 ￥680.00": Decimal("680.00"),
        }

        for text, expected in examples.items():
            with self.subTest(text=text):
                self.assertEqual(expected, find_total_amount(text))

    def test_machine_generated_pdf_returns_text_blocks(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            pdf_path = Path(temporary_directory) / "invoice.pdf"
            pdf_path.write_bytes(build_text_pdf("EIRI-INV-001"))
            analysis = analyze_document(pdf_path)
            extracted_text = "\n".join(
                block["text"] for block in analysis["textBlocks"]
            )
            self.assertIn("EIRI-INV-001", extracted_text)

    def test_real_invoice_returns_invoice_number_candidate(self) -> None:
        analysis = analyze_document(INVOICE_EXAMPLES / "example1.pdf")
        candidates = {
            candidate["field"]: candidate["value"]
            for candidate in analysis["candidates"]
        }
        self.assertEqual("25952000000269819544", candidates["invoice_number"])

    def test_real_invoice_returns_sales_merchant_candidate(self) -> None:
        analysis = analyze_document(INVOICE_EXAMPLES / "example1.pdf")
        candidates = {
            candidate["field"]: candidate["value"]
            for candidate in analysis["candidates"]
        }
        self.assertEqual("深圳德诺嘉电子有限公司", candidates["merchant_name"])

    def test_real_invoice_returns_price_tax_total_candidate(self) -> None:
        analysis = analyze_document(INVOICE_EXAMPLES / "example1.pdf")
        candidates = {
            candidate["field"]: candidate["value"]
            for candidate in analysis["candidates"]
        }
        self.assertEqual("778800", candidates["total_minor_units"])

    def test_all_invoice_examples_return_complete_high_confidence_fields(self) -> None:
        examples = {
            "example1.pdf": ("25952000000269819544", "778800"),
            "example2.pdf": ("25952000000270675013", "155324"),
            "example3.pdf": ("25952000000269826712", "210000"),
        }

        for file_name, (invoice_number, total_minor_units) in examples.items():
            with self.subTest(file_name=file_name):
                analysis = analyze_document(INVOICE_EXAMPLES / file_name)
                candidates_by_field = {
                    candidate["field"]: candidate
                    for candidate in analysis["candidates"]
                }
                self.assertEqual(
                    "深圳德诺嘉电子有限公司",
                    candidates_by_field["merchant_name"]["value"],
                )
                self.assertEqual(invoice_number, candidates_by_field["invoice_number"]["value"])
                self.assertEqual(
                    total_minor_units,
                    candidates_by_field["total_minor_units"]["value"],
                )
                for candidate in candidates_by_field.values():
                    self.assertGreaterEqual(candidate["confidence"], 0.98)
                    self.assertEqual("invoice-profile", candidate["source"])
                    self.assertEqual(1, candidate["page"])
                    self.assertGreater(candidate["bounds"]["width"], 0)
                self.assertFalse(analysis["needsReview"])

    def test_semantic_labels_extract_example4_without_coordinate_profile(self) -> None:
        analysis = analyze_document(INVOICE_EXAMPLES / "example4.pdf")
        candidates = {
            candidate["field"]: candidate["value"]
            for candidate in analysis["candidates"]
        }

        self.assertEqual("深圳嘉立创科技集团股份有限公司", candidates["merchant_name"])
        self.assertEqual("26957000000051824928", candidates["invoice_number"])
        self.assertEqual("507874", candidates["total_minor_units"])
        self.assertFalse(analysis["needsReview"])

    def test_real_invoices_return_project_names_in_document_order(self) -> None:
        expected_product_names = {
            "example1.pdf": ["*电子元件*BGA164-0.5-12*12-1.5合金翻盖旋钮老化座"],
            "example2.pdf": ["*电子元件*BGA164合金翻盖旋钮测试座"],
            "example3.pdf": [
                "*电子元件*BGA164-0.5-12*12-1.5-1A针板整套",
                "*电子元件*BGA164-0.5-12*12-1.5-2A针板整套",
            ],
            "example4.pdf": [
                "*印制电路板*PCBA-线路板",
                "*印制电路板*PCBA-元器件",
                "*印制电路板*PCBA-SMT贴片",
            ],
        }

        for file_name, expected in expected_product_names.items():
            with self.subTest(file_name=file_name):
                analysis = analyze_document(INVOICE_EXAMPLES / file_name)
                actual = [
                    candidate["value"]
                    for candidate in analysis["candidates"]
                    if candidate["field"] == "product_name"
                ]
                self.assertEqual(expected, actual)

    def test_image_only_invoice_uses_ocr_to_extract_fields(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            pdf_path = Path(temporary_directory) / "image-only-invoice.pdf"
            create_image_only_pdf(INVOICE_EXAMPLES / "example1.pdf", pdf_path)

            analysis = analyze_document(pdf_path)
            candidates = {
                candidate["field"]: candidate["value"]
                for candidate in analysis["candidates"]
            }

            self.assertTrue(analysis["textBlocks"])
            self.assertTrue(all(block["source"] == "ocr" for block in analysis["textBlocks"]))
            self.assertEqual("深圳德诺嘉电子有限公司", candidates["merchant_name"])
            self.assertEqual("25952000000269819544", candidates["invoice_number"])
            self.assertEqual("778800", candidates["total_minor_units"])
            self.assertFalse(analysis["needsReview"])


if __name__ == "__main__":
    unittest.main()
