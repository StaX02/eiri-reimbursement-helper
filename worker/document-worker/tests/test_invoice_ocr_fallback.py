import os
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfgen.canvas import Canvas

from eiri_document_worker.__main__ import (
    analyze_invoice_text,
    analyze_pdf,
    merge_invoice_analyses,
    usable_invoice_text,
)


TEXT = "发票号码：26952000002119699861\n名称：测试销售有限公司\n项目名称\n*电子设备*测试设备\n合计\n（小写）￥28.90"
OCR_TARGET = "eiri_document_worker.__main__.extract_ocr_text_blocks"


def ocr_result(text=TEXT, confidence=0.99, page=1):
    return ([{"text": text, "page": page, "source": "ocr", "confidence": confidence,
              "bounds": {"x": 10.0, "y": 10.0, "width": 500.0, "height": 300.0}}],
            {page: (600.0, 400.0)})


class InvoiceOcrFallbackTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        font = Path(os.environ.get("WINDIR", "C:/Windows")) / "Fonts/simsun.ttc"
        pdfmetrics.registerFont(TTFont("FallbackTestFont", str(font), subfontIndex=0))

    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.path = Path(temporary.name) / "invoice.pdf"

    def write_pdf(self, *pages):
        canvas = Canvas(str(self.path), pagesize=(600, 400))
        for text in pages:
            canvas.setFont("FallbackTestFont", 12)
            for index, line in enumerate(text.splitlines()):
                canvas.drawString(20, 380 - index * 20, line)
            canvas.showPage()
        canvas.save()

    def test_complete_native_invoice_skips_ocr(self):
        self.write_pdf(TEXT)
        with patch(OCR_TARGET) as ocr:
            analysis = analyze_pdf(self.path)
        ocr.assert_not_called()
        self.assertFalse(analysis["needsReview"])

    def test_watermark_or_garbled_text_falls_back(self):
        for text in ("Downloaded copy", "\ufffd" * 60):
            with self.subTest(text=text[:10]):
                self.write_pdf(text)
                with patch(OCR_TARGET, return_value=ocr_result()) as ocr:
                    analysis = analyze_pdf(self.path)
                ocr.assert_called_once()
                fields = {c["field"]: c["value"] for c in analysis["candidates"]}
                self.assertEqual("2890", fields["total_minor_units"])
                self.assertEqual("26952000002119699861", fields["invoice_number"])
                self.assertFalse(analysis["needsReview"])

    def test_missing_amount_uses_ocr_without_duplicating_products(self):
        self.write_pdf(TEXT.replace("（小写）￥28.90", "（小写）金额无法读取"))
        with patch(OCR_TARGET, return_value=ocr_result()):
            analysis = analyze_pdf(self.path)
        self.assertEqual(["2890"], [c["value"] for c in analysis["candidates"] if c["field"] == "total_minor_units"])
        self.assertEqual(["*电子设备*测试设备"], [c["value"] for c in analysis["candidates"] if c["field"] == "product_name"])
        self.assertFalse(analysis["needsReview"])

    def test_conflicting_invoice_number_preserves_native_and_requires_review(self):
        self.write_pdf(TEXT.replace("（小写）￥28.90", "（小写）金额无法读取"))
        with patch(OCR_TARGET, return_value=ocr_result(TEXT.replace("26952000002119699861", "26952000002119699862"))):
            analysis = analyze_pdf(self.path)
        self.assertEqual("26952000002119699861", next(c["value"] for c in analysis["candidates"] if c["field"] == "invoice_number"))
        self.assertTrue(analysis["needsReview"])

    def test_ocr_failure_or_empty_result_retains_native_fields_for_review(self):
        self.write_pdf(TEXT.replace("（小写）￥28.90", "（小写）金额无法读取"))
        for options in ({"side_effect": RuntimeError("OCR unavailable")}, {"return_value": ([], {})}):
            with self.subTest(options=options), patch(OCR_TARGET, **options):
                analysis = analyze_pdf(self.path)
            self.assertEqual("26952000002119699861", next(c["value"] for c in analysis["candidates"] if c["field"] == "invoice_number"))
            self.assertTrue(analysis["needsReview"])

    def test_low_confidence_ocr_requires_review(self):
        self.write_pdf("Downloaded copy")
        with patch(OCR_TARGET, return_value=ocr_result(confidence=0.70)):
            analysis = analyze_pdf(self.path)
        self.assertTrue(analysis["needsReview"])
        self.assertTrue(all(c["confidence"] <= 0.70 for c in analysis["candidates"]))

    def test_readable_ocr_does_not_certify_unrecovered_damaged_native_fields(self):
        # Inject decoded replacement characters directly: PDF font encoding
        # may discard them before the quality gate sees the extracted text.
        damaged_text = TEXT + "\n" + "\ufffd" * 150
        self.assertFalse(usable_invoice_text(damaged_text))
        native_blocks, _ = ocr_result(damaged_text)
        native_blocks[0]["source"] = "pdf-text"
        ocr_blocks, _ = ocr_result(TEXT.replace("（小写）￥28.90", "（小写）金额无法读取"))
        analysis = merge_invoice_analyses(
            analyze_invoice_text(native_blocks, [], {}),
            analyze_invoice_text(ocr_blocks, [], {}),
            {1},
        )
        self.assertTrue(analysis["needsReview"])
        self.assertEqual("2890", next(c["value"] for c in analysis["candidates"] if c["field"] == "total_minor_units"))

    def test_scanned_continuation_adds_products_without_replacing_native_page(self):
        self.write_pdf(TEXT, "")
        with patch(OCR_TARGET, return_value=ocr_result(TEXT.replace("测试设备", "续页设备"), page=2)) as ocr:
            analysis = analyze_pdf(self.path)
        ocr.assert_called_once()
        products = [(c["page"], c["value"]) for c in analysis["candidates"] if c["field"] == "product_name"]
        self.assertEqual([(1, "*电子设备*测试设备"), (2, "*电子设备*续页设备")], products)
        self.assertFalse(analysis["needsReview"])

    def test_unrelated_low_confidence_footer_does_not_downgrade_fields(self):
        self.write_pdf("Downloaded copy")
        blocks, sizes = ocr_result()
        blocks.append({**blocks[0], "text": "模糊页脚", "confidence": 0.30,
                       "bounds": {"x": 10.0, "y": 350.0, "width": 50.0, "height": 10.0}})
        with patch(OCR_TARGET, return_value=(blocks, sizes)):
            analysis = analyze_pdf(self.path)
        self.assertFalse(analysis["needsReview"])

    def test_scanned_invoice_with_partial_text_layer_recovers_real_fields(self):
        import pypdfium2 as pdfium
        from reportlab.lib.utils import ImageReader

        sample = Path(__file__).resolve().parents[3] / "examples/invoice/example1.pdf"
        document = pdfium.PdfDocument(sample)
        try:
            page = document[0]
            try:
                width, height = page.get_size()
                image = page.render(scale=300 / 72).to_pil().convert("RGB")
                canvas = Canvas(str(self.path), pagesize=(width, height))
                canvas.drawImage(ImageReader(image), 0, 0, width, height)
                layer = canvas.beginText(10, 10)
                layer.setTextRenderMode(3)
                layer.textOut("Downloaded copy")
                canvas.drawText(layer)
                canvas.save()
            finally:
                page.close()
        finally:
            document.close()
        analysis = analyze_pdf(self.path)
        self.assertEqual({"pdf-text", "ocr"}, {b["source"] for b in analysis["textBlocks"]})
        fields = {c["field"]: c["value"] for c in analysis["candidates"]}
        self.assertEqual("26952000002119699861", fields["invoice_number"])
        self.assertEqual("2890", fields["total_minor_units"])
        self.assertEqual("深圳市绿联科技股份有限公司", fields["merchant_name"])
        # Rasterizing this layout merges some table cells. Recover the identity
        # and amount, but retain manual review for its low-confidence OCR rows.
        self.assertTrue(analysis["needsReview"])
        self.assertTrue(any(c["confidence"] < 0.90 for c in analysis["candidates"]))


if __name__ == "__main__":
    unittest.main()
