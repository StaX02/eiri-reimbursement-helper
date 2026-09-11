"""Render the approved form cover and preserve original supporting PDF pages."""
from io import BytesIO
from pathlib import Path
from xml.sax.saxutils import escape

from PIL import Image, ImageOps
from pypdf import PdfReader, PdfWriter
from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER
from reportlab.lib.pagesizes import A4, landscape
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.utils import ImageReader
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfgen.canvas import Canvas
from reportlab.platypus import Paragraph, Table, TableStyle


def _paragraph(value: str, style: ParagraphStyle) -> Paragraph:
    text = escape(value).replace("\r\n", "\n").replace("\r", "\n").replace("\n", "<br/>")
    return Paragraph(text or "&#160;", style)


def _cover(job: dict) -> BytesIO:
    font_path = Path(job["fontPath"])
    if not font_path.is_file():
        raise ValueError("未找到 Windows 宋体 simsun.ttc，请安装宋体后重试。")
    pdfmetrics.registerFont(TTFont("EiriSimSun", str(font_path), subfontIndex=0))
    width, height = A4
    table_width = width * 0.8
    body = ParagraphStyle("body", fontName="EiriSimSun", fontSize=9, leading=13, wordWrap="CJK", textColor=colors.black)
    title_style = ParagraphStyle("title", parent=body, fontSize=15, leading=21, alignment=TA_CENTER)
    title = _paragraph(job["reimburser"] + "提交的日常报销（电子发票）", title_style)
    _, title_height = title.wrap(width - 72, height)
    table = Table([[_paragraph(field["name"], body), _paragraph(field["value"], body)] for field in job["fields"]],
                  colWidths=[table_width * 0.2, table_width * 0.8])
    table.setStyle(TableStyle([
        ("GRID", (0, 0), (-1, -1), 0.5, colors.black),
        ("BACKGROUND", (0, 0), (-1, -1), colors.white),
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("LEFTPADDING", (0, 0), (-1, -1), 6), ("RIGHTPADDING", (0, 0), (-1, -1), 6),
        ("TOPPADDING", (0, 0), (-1, -1), 6), ("BOTTOMPADDING", (0, 0), (-1, -1), 6),
    ]))
    _, table_height = table.wrap(table_width, height)
    top = height - 42
    table_top = top - title_height - 20
    if table_top - table_height < 36:
        raise ValueError("报销表单内容过长，无法以小五号字完整放入 A4 首页，请精简表单内容后重试。")
    output = BytesIO()
    canvas = Canvas(output, pagesize=A4)
    canvas.setFillColor(colors.white)
    canvas.rect(0, 0, width, height, fill=1, stroke=0)
    canvas.setFillColor(colors.black)
    title.drawOn(canvas, 36, top - title_height)
    table.drawOn(canvas, (width - table_width) / 2, table_top - table_height)
    canvas.showPage()
    canvas.save()
    output.seek(0)
    return output


def _image_page(path: Path) -> BytesIO:
    output = BytesIO()
    with Image.open(path) as original:
        image = ImageOps.exif_transpose(original)
        image.load()
        if image.mode not in ("RGB", "RGBA", "L"):
            image = image.convert("RGBA")
        page_size = landscape(A4) if image.width > image.height else A4
        width, height = page_size
        scale = min(width / image.width, height / image.height)
        drawn_width, drawn_height = image.width * scale, image.height * scale
        canvas = Canvas(output, pagesize=page_size)
        canvas.setFillColor(colors.white)
        canvas.rect(0, 0, width, height, fill=1, stroke=0)
        canvas.drawImage(ImageReader(image), (width - drawn_width) / 2, (height - drawn_height) / 2,
                         width=drawn_width, height=drawn_height, mask="auto")
        canvas.showPage()
        canvas.save()
    output.seek(0)
    return output


def export_approved_reimbursement(job: dict) -> None:
    destination = Path(job["destinationPath"])
    paths = [Path(path) for path in job["supportingMaterialPaths"]]
    if any(path.resolve() == destination.resolve() for path in paths):
        raise ValueError("导出路径不能覆盖原始辅助材料。")
    with PdfWriter() as writer:
        writer.append(PdfReader(_cover(job)))
        for path in paths:
            if path.suffix.lower() == ".pdf":
                reader = PdfReader(path)
                if reader.is_encrypted:
                    raise ValueError("辅助材料 PDF 已加密，请提供可直接打开的 PDF 后重试。")
                if not reader.pages:
                    raise ValueError("辅助材料 PDF 没有页面。")
                writer.append(reader, import_outline=False)
            elif path.suffix.lower() in (".png", ".jpg", ".jpeg"):
                writer.append(PdfReader(_image_page(path)))
            else:
                raise ValueError("辅助材料格式不受支持，请使用 PDF、PNG 或 JPEG。")
        writer.add_metadata({"/Title": job["reimburser"] + "提交的日常报销（电子发票）"})
        with destination.open("wb") as output:
            writer.write(output)
