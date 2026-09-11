"""Read the product-name column before flattening an invoice's text layer."""
import re
from typing import Any


ROW_START = re.compile(r"(?m)^[ \t]*[*＊][^*＊]+[*＊]")


def _label_boxes(text_page: Any, label: str) -> list[tuple[float, float, float, float]]:
    search = text_page.search(label)
    boxes = []
    try:
        while (match := search.get_next()) is not None:
            start, count = match
            characters = [text_page.get_charbox(i) for i in range(start, start + count)]
            boxes.append((min(b[0] for b in characters), min(b[1] for b in characters),
                          max(b[2] for b in characters), max(b[3] for b in characters)))
    finally:
        search.close()
    return boxes


def extract_product_column(text_page: Any, page_height: float) -> dict[str, Any] | None:
    """Return names and their column bounds; None means this layout is unknown.

    Coordinates come from the actual headers on each page, so portrait/landscape
    and scaled invoices use the same rules. PDFium uses bottom-up coordinates.
    """
    headers = _label_boxes(text_page, "项目名称")
    specifications = _label_boxes(text_page, "规格型号")
    totals = _label_boxes(text_page, "合计")
    for header in headers:
        header_height = header[3] - header[1]
        neighbor = next((box for box in specifications if box[0] > header[2]
                         and abs((box[1] + box[3] - header[1] - header[3]) / 2) < header_height), None)
        if neighbor is None:
            continue
        below = [box for box in totals if box[0] < neighbor[0] and box[3] < header[1]]
        if not below:
            continue
        total = max(below, key=lambda box: box[3])
        inset = header_height * 0.12
        top, bottom = header[1] - inset, total[3] + inset
        # The name header is centered over its column. Its center and the
        # category markers' left edge locate the column edge even when a wide
        # centered specification extends left of its own header text.
        markers = [box for symbol in ("*", "＊") for box in _label_boxes(text_page, symbol)
                   if box[0] < header[0] and bottom < (box[1] + box[3]) / 2 < top]
        left = min((box[0] for box in markers), default=0.0)
        right = min(neighbor[0], header[0] + header[2] - left) - inset
        if right <= 0 or bottom >= top:
            continue
        text = text_page.get_text_bounded(left=0, right=right, bottom=bottom, top=top)
        starts = list(ROW_START.finditer(text))
        names = []
        for index, match in enumerate(starts):
            end = starts[index + 1].start() if index + 1 < len(starts) else len(text)
            # All remaining text is inside the name column. Preserve digits,
            # parentheses and punctuation that really belong to a product name.
            name = "".join(line.strip() for line in text[match.start():end].splitlines())
            name = name.replace("＊", "*")
            if name != "".join(line.strip() for line in match.group(0).splitlines()).replace("＊", "*"):
                names.append(name)
        return {"names": names, "bounds": {"x": 0.0, "y": page_height - top,
                                            "width": right, "height": top - bottom}}
    return None
