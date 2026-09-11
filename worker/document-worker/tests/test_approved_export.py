import os
import tempfile
import unittest
from pathlib import Path

from PIL import Image
from pypdf import PdfReader
from reportlab.pdfgen.canvas import Canvas
from eiri_document_worker.__main__ import handle_request


class ApprovedExportTests(unittest.TestCase):
    def job(self, root):
        return {"destinationPath": str(root / "output.pdf"), "fontPath": str(Path(os.environ.get("WINDIR", "C:/Windows")) / "Fonts/simsun.ttc"),
                "reimburser": "测试报销人", "fields": [{"name": n, "value": "测试内容"} for n in
                ["审批编号", "创建人", "创建人部门", "研究方向", "申请日期", "报销人", "报销类型", "报销内容", "总金额", "收款人名称", "收款人账号", "开户行名称", "备注"]],
                "supportingMaterialPaths": []}

    def export(self, job):
        return handle_request({"protocolVersion": 1, "operation": "exportApprovedReimbursement", "job": job})

    def test_cover_and_original_pdf_and_oriented_images(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "original.pdf"
            canvas = Canvas(str(source), pagesize=(420, 300))
            canvas.drawString(20, 50, "original page one")
            canvas.showPage()
            canvas.setPageSize((300, 500))
            canvas.drawString(20, 50, "original page two")
            canvas.showPage()
            canvas.save()
            portrait, wide = root / "portrait.png", root / "wide.jpg"
            Image.new("RGB", (100, 200), "red").save(portrait)
            Image.new("RGB", (300, 100), "blue").save(wide)
            job = self.job(root)
            job["fields"][7]["value"] = "含有 <标签> & 换行\n" + "自动换行内容" * 20
            job["supportingMaterialPaths"] = [str(source), str(portrait), str(wide)]
            self.assertTrue(self.export(job)["written"])
            pages = PdfReader(job["destinationPath"]).pages
            self.assertEqual(len(pages), 5)
            text = pages[0].extract_text()
            self.assertIn("测试报销人提交的日常报销（电子发票）", text)
            for field in job["fields"]:
                self.assertIn(field["name"], text)
            self.assertIn("含有 <标签> & 换行", text)
            for i, original in enumerate(PdfReader(source).pages, 1):
                self.assertEqual(pages[i].mediabox, original.mediabox)
                self.assertEqual(pages[i].get_contents().get_data(), original.get_contents().get_data())
            self.assertAlmostEqual(float(pages[0].mediabox.width), 595.2756, places=3)
            self.assertAlmostEqual(float(pages[0].mediabox.height), 841.8898, places=3)
            self.assertAlmostEqual(float(pages[3].mediabox.width), 595.2756, places=3)
            self.assertAlmostEqual(float(pages[4].mediabox.width), 841.8898, places=3)
            # Image placement uses a uniform scale and stays within the page (no cropping).
            for page, ratio in [(pages[3], 0.5), (pages[4], 3)]:
                placements = [args for args, op in page.get_contents().operations if op == b"cm" and float(args[0]) > 1]
                matrix = placements[-1]
                self.assertAlmostEqual(float(matrix[0]) / float(matrix[3]), ratio, places=4)
                self.assertGreaterEqual(float(matrix[4]), 0)
                self.assertGreaterEqual(float(matrix[5]), 0)
                self.assertLessEqual(float(matrix[0]) + float(matrix[4]), float(page.mediabox.width) + .001)
                self.assertLessEqual(float(matrix[3]) + float(matrix[5]), float(page.mediabox.height) + .001)

    def test_long_cover_fails_without_truncating_or_overwriting(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            job = self.job(root)
            Path(job["destinationPath"]).write_bytes(b"existing PDF")
            job["fields"][7]["value"] = "长内容" * 3000
            with self.assertRaisesRegex(ValueError, "内容过长"):
                self.export(job)
            self.assertEqual(Path(job["destinationPath"]).read_bytes(), b"existing PDF")

    def test_bad_material_does_not_overwrite_existing_pdf(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            job = self.job(root)
            Path(job["destinationPath"]).write_bytes(b"existing PDF")
            bad = root / "bad.pdf"
            bad.write_bytes(b"broken")
            job["supportingMaterialPaths"] = [str(bad)]
            with self.assertRaises(Exception):
                self.export(job)
            self.assertEqual(Path(job["destinationPath"]).read_bytes(), b"existing PDF")


if __name__ == "__main__":
    unittest.main()
