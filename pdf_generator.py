import io
import os
import platform
import urllib.request
from reportlab.lib.pagesizes import A4
from reportlab.lib import colors
from reportlab.lib.units import mm
from reportlab.platypus import (
    SimpleDocTemplate, Table, TableStyle, Paragraph, Spacer
)
from reportlab.lib.styles import ParagraphStyle
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont

FONT_NAME = "KorFont"
FONT_BOLD = "KorFontBold"

_font_registered = False


def _get_font_paths():
    """OS별 한글 폰트 경로 반환. 없으면 NanumGothic 자동 다운로드."""
    if platform.system() == "Windows":
        reg   = r"C:\Windows\Fonts\malgun.ttf"
        bold  = r"C:\Windows\Fonts\malgunbd.ttf"
        if os.path.exists(reg):
            return reg, bold if os.path.exists(bold) else reg

    # Linux / 클라우드 서버 — 시스템 Nanum 폰트 확인
    candidates = [
        "/usr/share/fonts/truetype/nanum/NanumGothic.ttf",
        "/usr/share/fonts/nanum/NanumGothic.ttf",
        "/usr/share/fonts/truetype/nanum/NanumGothicBold.ttf",
    ]
    for c in candidates:
        if os.path.exists(c):
            bold_path = c.replace("NanumGothic.ttf", "NanumGothicBold.ttf")
            return c, bold_path if os.path.exists(bold_path) else c

    # 없으면 프로젝트 fonts/ 폴더에 다운로드
    fonts_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "fonts")
    os.makedirs(fonts_dir, exist_ok=True)

    reg_path  = os.path.join(fonts_dir, "NanumGothic-Regular.ttf")
    bold_path = os.path.join(fonts_dir, "NanumGothic-Bold.ttf")

    _download_font(
        reg_path,
        "https://github.com/google/fonts/raw/main/ofl/nanumgothic/NanumGothic-Regular.ttf"
    )
    _download_font(
        bold_path,
        "https://github.com/google/fonts/raw/main/ofl/nanumgothic/NanumGothic-Bold.ttf"
    )
    return reg_path, bold_path


def _download_font(dest, url):
    if not os.path.exists(dest):
        print(f"[PDF] 한글 폰트 다운로드 중: {url}")
        try:
            urllib.request.urlretrieve(url, dest)
            print(f"[PDF] 완료: {dest}")
        except Exception as e:
            print(f"[PDF] 폰트 다운로드 실패: {e}")


def _register_fonts():
    global _font_registered
    if _font_registered:
        return
    reg, bold = _get_font_paths()
    if os.path.exists(reg):
        pdfmetrics.registerFont(TTFont(FONT_NAME, reg))
        pdfmetrics.registerFont(TTFont(FONT_BOLD, bold))
    else:
        # 최후 수단: ReportLab 내장 Helvetica (한글 깨짐 가능성 있음)
        FONT_NAME = "Helvetica"
        FONT_BOLD = "Helvetica-Bold"
    _font_registered = True


def _style(size=10, bold=False, align="LEFT", color=colors.black):
    _register_fonts()
    fn = FONT_BOLD if bold else FONT_NAME
    return ParagraphStyle(
        name=f"s{size}{bold}{align}",
        fontName=fn,
        fontSize=size,
        textColor=color,
        alignment={"LEFT": 0, "CENTER": 1, "RIGHT": 2}.get(align, 0),
        leading=size * 1.4,
    )


def _fmt(n):
    try:
        return f"{int(n):,}"
    except (ValueError, TypeError):
        return str(n)


def _build_story(quotation: dict, line_items: list, company: dict):
    """ReportLab Platypus story 리스트 반환."""
    _register_fonts()
    W = A4[0] - 40 * mm
    story = []

    # 제목
    story.append(Paragraph("견  적  서", _style(22, bold=True, align="CENTER")))
    story.append(Spacer(1, 6 * mm))

    # 회사 정보 + 견적 정보
    co_name  = company.get("company_name") or "샤시크루"
    co_addr  = company.get("address") or ""
    co_phone = company.get("phone") or ""
    co_fax   = company.get("fax") or ""
    co_mgr   = company.get("manager") or ""

    left = [
        [Paragraph(f"<b>{co_name}</b>", _style(11, bold=True))],
        [Paragraph(co_addr, _style(9))],
        [Paragraph(f"TEL: {co_phone}  /  FAX: {co_fax}", _style(9))],
        [Paragraph(f"담당: {co_mgr}", _style(9))],
    ]
    right = [
        [Paragraph(f"견적번호: {quotation.get('quote_number', '')}", _style(9))],
        [Paragraph(f"업 체: {quotation.get('vendor_name', '')}", _style(9))],
        [Paragraph(f"작성일: {quotation.get('issue_date', '')}", _style(9))],
        [Paragraph(f"유효기간: {quotation.get('valid_until', '')}", _style(9))],
    ]
    hdr = Table([[Table(left, colWidths=[W * 0.6]), Table(right, colWidths=[W * 0.4])]],
                colWidths=[W * 0.6, W * 0.4])
    hdr.setStyle(TableStyle([
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("LEFTPADDING", (0, 0), (-1, -1), 0),
        ("RIGHTPADDING", (0, 0), (-1, -1), 0),
    ]))
    story.append(hdr)
    story.append(Spacer(1, 4 * mm))

    # 구분선
    line_t = Table([[""]], colWidths=[W])
    line_t.setStyle(TableStyle([
        ("LINEBELOW", (0, 0), (-1, -1), 1.5, colors.HexColor("#2c3e50")),
        ("TOPPADDING", (0, 0), (-1, -1), 0),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 0),
    ]))
    story.append(line_t)
    story.append(Spacer(1, 4 * mm))

    # 수신처
    cust       = quotation.get("customer_company") or ""
    cust_mgr   = quotation.get("customer_name") or ""
    cust_phone = quotation.get("customer_phone") or ""
    recv = Table([[
        Paragraph(f"수신: <b>{cust}</b>", _style(11)),
        Paragraph(f"담당자: {cust_mgr}  /  연락처: {cust_phone}", _style(9, align="RIGHT")),
    ]], colWidths=[W * 0.55, W * 0.45])
    recv.setStyle(TableStyle([
        ("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
        ("LEFTPADDING", (0, 0), (-1, -1), 0),
        ("RIGHTPADDING", (0, 0), (-1, -1), 0),
    ]))
    story.append(recv)
    story.append(Spacer(1, 5 * mm))

    # 품목 테이블
    col_w = [W * r for r in [0.05, 0.22, 0.18, 0.07, 0.15, 0.15, 0.18]]
    th = _style(9, bold=True, align="CENTER", color=colors.white)
    td  = _style(9, align="CENTER")
    tdl = _style(9, align="LEFT")
    tdr = _style(9, align="RIGHT")

    rows = [[
        Paragraph("No", th), Paragraph("품명", th), Paragraph("사이즈", th),
        Paragraph("수량", th), Paragraph("단가", th), Paragraph("금액", th),
        Paragraph("비고", th),
    ]]
    for i, item in enumerate(line_items, 1):
        rows.append([
            Paragraph(str(i), td),
            Paragraph(item.get("name") or "", tdl),
            Paragraph(item.get("size") or "", td),
            Paragraph(str(item.get("quantity") or 0), td),
            Paragraph(_fmt(item.get("unit_price") or 0), tdr),
            Paragraph(_fmt(item.get("amount") or 0), tdr),
            Paragraph(item.get("remarks") or "", tdl),
        ])
    while len(rows) < 10:
        rows.append([Paragraph("", td)] * 7)

    item_t = Table(rows, colWidths=col_w, repeatRows=1)
    item_t.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, 0), colors.HexColor("#2c3e50")),
        ("TEXTCOLOR",  (0, 0), (-1, 0), colors.white),
        ("GRID", (0, 0), (-1, -1), 0.5, colors.HexColor("#cccccc")),
        ("ROWBACKGROUNDS", (0, 1), (-1, -1), [colors.white, colors.HexColor("#f8f9fa")]),
        ("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
        ("TOPPADDING", (0, 0), (-1, -1), 4),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 4),
        ("LEFTPADDING", (0, 0), (-1, -1), 4),
        ("RIGHTPADDING", (0, 0), (-1, -1), 4),
    ]))
    story.append(item_t)
    story.append(Spacer(1, 4 * mm))

    # 합계
    supply = quotation.get("supply_total") or 0
    vat    = quotation.get("vat") or 0
    grand  = quotation.get("grand_total") or 0
    total_data = [
        ["", Paragraph("공  급  가  액", _style(10, bold=True, align="RIGHT")),
         Paragraph(f"{_fmt(supply)} 원", _style(10, align="RIGHT"))],
        ["", Paragraph("부    가    세", _style(10, bold=True, align="RIGHT")),
         Paragraph(f"{_fmt(vat)} 원", _style(10, align="RIGHT"))],
        ["", Paragraph("합         계", _style(11, bold=True, align="RIGHT")),
         Paragraph(f"{_fmt(grand)} 원", _style(11, bold=True, align="RIGHT"))],
    ]
    total_t = Table(total_data, colWidths=[W * 0.55, W * 0.25, W * 0.20])
    total_t.setStyle(TableStyle([
        ("LINEBELOW", (1, 1), (2, 1), 0.5, colors.HexColor("#cccccc")),
        ("LINEABOVE", (1, 2), (2, 2), 1.5, colors.HexColor("#2c3e50")),
        ("LINEBELOW", (1, 2), (2, 2), 1.5, colors.HexColor("#2c3e50")),
        ("TOPPADDING",    (0, 0), (-1, -1), 3),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 3),
        ("LEFTPADDING",   (0, 0), (-1, -1), 4),
        ("RIGHTPADDING",  (0, 0), (-1, -1), 4),
    ]))
    story.append(total_t)

    # 특기사항
    notes = quotation.get("notes") or ""
    if notes:
        story.append(Spacer(1, 5 * mm))
        notes_data = [
            [Paragraph("특기사항", _style(9, bold=True, color=colors.white))],
            [Paragraph(notes, _style(9))],
        ]
        notes_t = Table(notes_data, colWidths=[W])
        notes_t.setStyle(TableStyle([
            ("BACKGROUND", (0, 0), (-1, 0), colors.HexColor("#2c3e50")),
            ("GRID", (0, 0), (-1, -1), 0.5, colors.HexColor("#cccccc")),
            ("TOPPADDING",    (0, 0), (-1, -1), 4),
            ("BOTTOMPADDING", (0, 0), (-1, -1), 4),
            ("LEFTPADDING",   (0, 0), (-1, -1), 6),
            ("RIGHTPADDING",  (0, 0), (-1, -1), 6),
        ]))
        story.append(notes_t)

    return story


def generate_pdf_bytes(quotation: dict, line_items: list, company: dict) -> bytes:
    """PDF를 메모리 버퍼에 생성하여 bytes 반환 (클라우드용)."""
    buf = io.BytesIO()
    doc = SimpleDocTemplate(
        buf, pagesize=A4,
        leftMargin=20*mm, rightMargin=20*mm,
        topMargin=15*mm, bottomMargin=15*mm,
    )
    doc.build(_build_story(quotation, line_items, company))
    return buf.getvalue()


def generate_pdf(save_path: str, quotation: dict, line_items: list, company: dict) -> str:
    """PDF를 파일로 저장하고 경로 반환 (로컬용 호환)."""
    os.makedirs(save_path, exist_ok=True)
    safe = "".join(c for c in (quotation.get("customer_company") or "거래처")
                   if c.isalnum() or c in ("-", "_", " "))
    filename = f"{quotation['quote_number']}_{safe.strip()}.pdf"
    filepath = os.path.join(save_path, filename)

    pdf_bytes = generate_pdf_bytes(quotation, line_items, company)
    with open(filepath, "wb") as f:
        f.write(pdf_bytes)
    return filepath
