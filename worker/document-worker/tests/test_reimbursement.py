import unittest
from pathlib import Path
from unittest.mock import patch
from eiri_document_worker.__main__ import analyze_reimbursement_pdf, reimbursement_candidates, handle_request

SAMPLE = next((Path(__file__).resolve().parents[3] / 'examples' / 'reb').glob('*.pdf'), None)

class ReimbursementTests(unittest.TestCase):
    @unittest.skipUnless(SAMPLE, "Local reimbursement sample is not present")
    def test_sample_first_page_semantic_fields(self):
        result = handle_request({'protocolVersion': 1, 'job': {'kind': 3, 'filePath': str(SAMPLE)}})['analysis']
        fields = {c['field']: c['value'] for c in result['candidates']}
        self.assertEqual(fields, {'application_date': '2026-08-25', 'reimbursement_type': '材料费',
            'reimbursement_content': 'IGCT驱动芯片测试PCB', 'total_minor_units': '507874'})
        self.assertFalse(result['needsReview'])
        self.assertTrue(all(b['page'] == 1 for b in result['textBlocks']))

    def test_label_semantics_ignore_creation_and_approval_dates(self):
        text = '创建时间 2025-01-01\n申 请 日 期：2026年8月25日\n报销类型\n材料费\n报销内容：PCB\n总金额（元）：￥5,078.74\n审批日期 2026-09-01'
        fields = {c['field']: c['value'] for c in reimbursement_candidates(text, 'ocr', {})}
        self.assertEqual(fields['application_date'], '2026-08-25')
        self.assertEqual(fields['reimbursement_type'], '材料费')
        self.assertEqual(fields['total_minor_units'], '507874')

    def test_content_stops_at_labeled_amount_on_same_line(self):
        fields = {c['field']: c['value'] for c in reimbursement_candidates('报销内容：芯片采购 总金额：123.45', 'pdf-text', {})}
        self.assertEqual(fields['reimbursement_content'], '芯片采购')
        self.assertEqual(fields['total_minor_units'], '12345')

    def test_multiline_content_continues_until_next_field(self):
        fields = {c['field']: c['value'] for c in reimbursement_candidates('报销内容：芯片采购\n及测试材料\n总金额（元） 10.50', 'pdf-text', {})}
        self.assertEqual(fields['reimbursement_content'], '芯片采购\n及测试材料')
        self.assertEqual(fields['total_minor_units'], '1050')

    @unittest.skipUnless(SAMPLE, "Local reimbursement sample is not present")
    def test_missing_text_fields_ocr_only_first_page(self):
        with patch('eiri_document_worker.__main__.reimbursement_candidates', return_value=[]), patch('eiri_document_worker.__main__.extract_ocr_text_blocks', return_value=([], {})) as ocr:
            result = analyze_reimbursement_pdf(SAMPLE)
        ocr.assert_called_once_with(SAMPLE, page_limit=1)
        self.assertTrue(result['needsReview'])

if __name__ == '__main__':
    unittest.main()
