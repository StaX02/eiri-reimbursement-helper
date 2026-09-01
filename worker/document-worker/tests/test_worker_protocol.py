import json
import subprocess
import sys
import tempfile
import unittest
import uuid
from pathlib import Path


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


class WorkerProtocolTests(unittest.TestCase):
    def test_machine_generated_pdf_returns_text_blocks(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            pdf_path = Path(temporary_directory) / "invoice.pdf"
            pdf_path.write_bytes(build_text_pdf("EIRI-INV-001"))
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

            self.assertEqual(0, completed.returncode, completed.stderr)
            response = json.loads(completed.stdout)
            extracted_text = "\n".join(
                block["text"] for block in response["analysis"]["textBlocks"]
            )
            self.assertIn("EIRI-INV-001", extracted_text)

    def test_real_invoice_returns_invoice_number_candidate(self) -> None:
        request = {
            "protocolVersion": 1,
            "job": {
                "jobId": str(uuid.uuid4()),
                "filePath": str(INVOICE_EXAMPLES / "example1.pdf"),
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

        self.assertEqual(0, completed.returncode, completed.stderr)
        response = json.loads(completed.stdout)
        candidates = {
            candidate["field"]: candidate["value"]
            for candidate in response["analysis"]["candidates"]
        }
        self.assertEqual("25952000000269819544", candidates["invoice_number"])

    def test_real_invoice_returns_sales_merchant_candidate(self) -> None:
        request = {
            "protocolVersion": 1,
            "job": {
                "jobId": str(uuid.uuid4()),
                "filePath": str(INVOICE_EXAMPLES / "example1.pdf"),
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

        self.assertEqual(0, completed.returncode, completed.stderr)
        response = json.loads(completed.stdout)
        candidates = {
            candidate["field"]: candidate["value"]
            for candidate in response["analysis"]["candidates"]
        }
        self.assertEqual("深圳德诺嘉电子有限公司", candidates["merchant_name"])

    def test_real_invoice_returns_price_tax_total_candidate(self) -> None:
        request = {
            "protocolVersion": 1,
            "job": {
                "jobId": str(uuid.uuid4()),
                "filePath": str(INVOICE_EXAMPLES / "example1.pdf"),
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

        self.assertEqual(0, completed.returncode, completed.stderr)
        response = json.loads(completed.stdout)
        candidates = {
            candidate["field"]: candidate["value"]
            for candidate in response["analysis"]["candidates"]
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
                request = {
                    "protocolVersion": 1,
                    "job": {
                        "jobId": str(uuid.uuid4()),
                        "filePath": str(INVOICE_EXAMPLES / file_name),
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

                self.assertEqual(0, completed.returncode, completed.stderr)
                analysis = json.loads(completed.stdout)["analysis"]
                candidates = {
                    candidate["field"]: candidate["value"]
                    for candidate in analysis["candidates"]
                }
                self.assertEqual("深圳德诺嘉电子有限公司", candidates["merchant_name"])
                self.assertEqual(invoice_number, candidates["invoice_number"])
                self.assertEqual(total_minor_units, candidates["total_minor_units"])
                self.assertFalse(analysis["needsReview"])


if __name__ == "__main__":
    unittest.main()
