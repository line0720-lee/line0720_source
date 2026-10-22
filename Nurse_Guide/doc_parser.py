"""간호 규정 문서 → 조항 단위 청크 파서.

블록 모델: {"text": str, "page": int|None, "heading": bool} — 줄(단락)마다 하나, 표는 행마다 "a | b | c".
청크 우선순위: ① 제N조(제목) 조항 → ② 제목(Heading 스타일·번호 제목) → ③ 30줄/5줄 겹침 줄 청크.
"""
import os
import re
from datetime import date

import fitz  # PyMuPDF

# ── 정규식·상수 ──────────────────────────────────────────
TEXT_EXTS = {'txt', 'md', 'markdown', 'csv', 'log', 'json'}
SUPPORTED_EXTS = TEXT_EXTS | {'docx', 'pdf'}

CHAPTER_RE = re.compile(r'^제\s*(\d+)\s*장(?:\s+(.*))?$')                  # "제2장에서 …" 같은 참조 문장은 제외
ARTICLE_RE = re.compile(r'^제\s*(\d+)\s*조(?:의\s*(\d+))?\s*[\(（]([^\)）]*)[\)）]'
                        r'(?![에의을를로은는이가와과도만])')                    # "제7조(…)에 따른다" 같은 참조 제외
HEADING_RE = re.compile(r'^(\d{1,2}[\.\)]|[가나다라마바사아자차카타파하][\.\)])\s*\S.{0,40}$')  # [가-하] 범위는 거의 모든 한글이라 목록으로
PAGE_NUM_RE = re.compile(r'^[-–—\s]*\d+\s*[-–—\s]*$')        # "- 2 -", "2" 같은 쪽번호 모양 줄
DASH_PAGE_RE = re.compile(r'^[-–—]\s*\d+\s*[-–—]$')           # "- 2 -" → 항상 쪽번호
BARE_NUM_RE = re.compile(r'^\d{1,4}$')                          # "2" → 페이지 첫·끝 줄일 때만 쪽번호
CLAUSE_RE = re.compile(r'^[\u2460-\u2473]')                  # ①~⑳ 항 시작
DATE = r'(?<!\d)(\d{4})\s*(?:[-./]|년)\s*(\d{1,2})\s*(?:[-./]|월)\s*(\d{1,2})'
REVISED_RE = re.compile(r'(?:개정일|개정|시행일)[^\n]{0,15}?' + DATE)   # "개정 제3차 2026.01.01" 도 허용
NEXT_DATE_RE = re.compile(r'[\s|,~·()\[\]\-]*' + DATE)               # 구분자만 사이에 둔 이어지는 날짜 (개정 이력 표)

ARTICLE_LIMIT = 1500   # 조항 본문이 이 글자 수를 넘으면 항 단위로 나눈다
LINE_CHUNK, LINE_OVERLAP = 30, 5
MIN_UNTITLED = 20      # 제목 없는 구간(머리말 등)은 이 글자 수 이상일 때만 청크로 만든다


def _block(text, page=None, heading=False):
    return {"text": text, "page": page, "heading": heading}


# ── 파일 → 블록 ──────────────────────────────────────────
def docx_blocks(path):
    """본문 단락·표를 문서 순서대로 블록화. 표는 행마다 '셀1 | 셀2 | 셀3'."""
    from docx import Document
    from docx.table import Table
    from docx.text.paragraph import Paragraph
    doc, out = Document(path), []
    for el in doc.element.body.iterchildren():
        tag = el.tag.rsplit('}', 1)[-1]
        if tag == 'p':
            p = Paragraph(el, doc)
            style = (p.style.name if p.style is not None else "") or ""
            head = style.startswith(("Heading", "제목"))
            for line in p.text.split("\n"):          # 단락 안 줄바꿈(Shift+Enter)도 줄로 나눈다
                if line.strip():
                    out.append(_block(line.strip(), None, head))
        elif tag == 'tbl':
            for row in Table(el, doc).rows:
                cells, seen = [], []
                for c in row.cells:
                    if any(c._tc is t for t in seen):   # 병합 셀은 같은 셀이 반복되므로 한 번만
                        continue
                    seen.append(c._tc)
                    cells.append(" ".join(c.text.split()))
                if any(cells):
                    out.append(_block(" | ".join(cells)))
    return out


def pdf_page_lines(page):
    """PDF 한 페이지 → 줄 목록. 텍스트 레이어(PyMuPDF) 우선, 거의 비면 OCR(Tesseract) 폴백."""
    # ① 텍스트 레이어 추출
    text = page.get_text("text").strip()
    lines = [l.strip() for l in text.splitlines() if l.strip()]

    # ② 텍스트가 거의 없으면 OCR 시도
    if len(lines) < 3:
        try:
            import pytesseract
            from PIL import Image
            mat = fitz.Matrix(2, 2)          # 2× 해상도
            pix = page.get_pixmap(matrix=mat)
            img = Image.frombytes("RGB", [pix.width, pix.height], pix.samples)
            ocr_text = pytesseract.image_to_string(img, lang="kor+eng")
            ocr_lines = [l.strip() for l in ocr_text.splitlines() if l.strip()]
            if ocr_lines:
                lines = ocr_lines
        except Exception:
            pass   # Tesseract 미설치 시 무시
    return lines


def _filter_page_lines(lines):
    """쪽번호 줄 제거: '- 2 -' 꼴은 항상, 숫자만 있는 줄은 페이지 첫·끝 줄일 때만 (표 셀 숫자는 남긴다)."""
    last = len(lines) - 1
    return [l for i, l in enumerate(lines)
            if not DASH_PAGE_RE.match(l) and not (i in (0, last) and BARE_NUM_RE.match(l))]


def pdf_blocks(path):
    """PDF → 블록 (page 1부터). 쪽번호만 있는 줄은 버린다."""
    out = []
    with fitz.open(path) as doc:
        for page_num, page in enumerate(doc, start=1):
            out += [_block(l, page_num) for l in _filter_page_lines(pdf_page_lines(page))]
    return out


def text_blocks(path):
    with open(path, encoding='utf-8', errors='ignore') as f:
        return [_block(l.strip()) for l in f.read().splitlines() if l.strip()]


# ── 메타데이터 ───────────────────────────────────────────
def _date(m):
    try:
        return date(*(int(g) for g in m.groups())).isoformat()
    except ValueError:   # 2026-02-31 같은 없는 날짜
        return ""


def find_revised(blocks):
    """앞 30블록에서 개정일/개정/시행일 + 날짜 중 가장 최근 날짜 → 'YYYY-MM-DD' (없으면 '').
    '개정이력 | 2019.01.01 | 2026.01.01' 처럼 구분자만 사이에 둔 이어지는 날짜도 후보로 본다."""
    found = []
    for b in blocks[:30]:
        text = b["text"]
        for m in REVISED_RE.finditer(text):
            while m:
                found.append(_date(m))
                m = NEXT_DATE_RE.match(text, m.end())
    return max(found, default="")


# ── 긴 조항 분할 ─────────────────────────────────────────
def split_long_article(text, limit=1500):
    """긴 조항 본문 하나를 항(①②…) 경계에서 여러 조각으로 나눈다.

    입력: 조항 본문 하나 (줄들을 "\\n" 으로 이어 붙인 문자열, 조항 제목 줄은 빠져 있음).
    규칙:
      - len(text) <= limit 이면 [text] 를 그대로 돌려준다.
      - 원문자 ①~⑳(U+2460~U+2473, CLAUSE_RE)로 시작하는 줄이 새 항의 시작이다.
        첫 항 앞의 줄(머리 문장)은 첫 항에, 항 다음의 일반 줄(1. 2. 호 등)은 바로 앞 항에 붙는다.
      - 연속된 항을 앞에서부터 욕심껏(greedy) 묶어 조각 길이("\\n" 포함)가 limit 이하가 되게 한다.
      - 항 하나가 혼자서 limit 를 넘으면 그 항만으로 한 조각이 된다 (더 쪼개지 않음).
      - 줄 중간에서는 절대 자르지 않는다. "\\n".join(결과) == text 가 되어야 한다.
    출력: 조각 문자열 리스트.
    """
    if len(text) <= limit:
        return [text]
    groups = [[]]                                    # 항 단위 줄 묶음
    for line in text.split("\n"):
        if CLAUSE_RE.match(line) and any(CLAUSE_RE.match(l) for l in groups[-1]):
            groups.append([])                        # 이미 항이 들어 있는 묶음에서만 새 항 시작
        groups[-1].append(line)
    pieces = []
    for g in ("\n".join(g) for g in groups):
        if pieces and len(pieces[-1]) + 1 + len(g) <= limit:
            pieces[-1] += "\n" + g
        else:
            pieces.append(g)
    return pieces


# ── 블록 → 청크 ──────────────────────────────────────────
def _lines(blocks):
    """블록 안 줄바꿈까지 줄 단위로 펼친다."""
    out = []
    for b in blocks:
        for l in str(b.get("text", "")).split("\n"):
            if l.strip():
                out.append(_block(l.strip(), b.get("page"), bool(b.get("heading"))))
    return out


def _norm_chapter(m):
    return f"제{m.group(1)}장 {' '.join((m.group(2) or '').split())}".strip()


def _norm_article(m):
    return f"제{m.group(1)}조" + (f"의{m.group(2)}" if m.group(2) else "") + f"({' '.join(m.group(3).split())})"


def _is_heading(l):
    t = l["text"]
    return not CHAPTER_RE.match(t) and (l["heading"] or (len(t) <= 40 and bool(HEADING_RE.match(t))))


def _pages(pages):
    pages = [p for p in pages if p is not None]
    return (min(pages), max(pages)) if pages else (None, None)


def _make(doc, file, chapter, article, part, pages, revised, text):
    ps, pe = _pages(pages)
    head = " > ".join(x for x in (doc, chapter, article) if x) + (f" ({part})" if part else "")
    return {"id": "", "file": file, "doc": doc, "chapter": chapter, "article": article, "part": part,
            "page_start": ps, "page_end": pe, "revised": revised, "text": text,
            "embed_text": f"{head}\n{text}"}


def _line_chunks(lines, doc, file, revised):
    """조항·제목이 전혀 없는 문서: 30줄 청크, 5줄 겹침."""
    out, step = [], LINE_CHUNK - LINE_OVERLAP
    for s in range(0, len(lines), step):
        seg = lines[s:s + LINE_CHUNK]
        c = _make(doc, file, "", "", "", [l["page"] for l in seg], revised, "\n".join(l["text"] for l in seg))
        c["lines"] = f"{s + 1}~{s + len(seg)}"
        out.append(c)
        if s + LINE_CHUNK >= len(lines):
            break
    return out


def _section_chunks(sec, doc, file, revised):
    """구간 하나 → 청크(들). 본문이 길면 split_long_article 로 나눠 part 'i/n' 을 붙인다."""
    body = sec["lines"]
    text = "\n".join(l["text"] for l in body)
    if sec["article"] in ("", "머리말") and len(text) < MIN_UNTITLED:
        return []
    title_pages = [sec["page"]]
    pieces = split_long_article(text, ARTICLE_LIMIT) if len(text) > ARTICLE_LIMIT else [text]
    if len(pieces) <= 1:
        return [_make(doc, file, sec["chapter"], sec["article"], "", title_pages + [l["page"] for l in body], revised, text)]

    # 조각마다 들어간 줄을 앞에서부터 대응시켜 페이지를 계산 (줄이 안 맞으면 조항 전체 범위로 대체)
    full = title_pages + [l["page"] for l in body]
    spans, i = [], 0
    for p in pieces:
        n = len(p.split("\n"))
        seg = body[i:i + n]
        if [l["text"] for l in seg] != p.split("\n"):
            spans = None
            break
        spans.append([l["page"] for l in seg])
        i += n
    if spans is None or i != len(body):
        spans = [full] * len(pieces)
    else:
        spans[0] = title_pages + spans[0]
    n = len(pieces)
    return [_make(doc, file, sec["chapter"], sec["article"], f"{k}/{n}", spans[k - 1], revised, p)
            for k, p in enumerate(pieces, start=1)]


def split_articles(blocks, doc, file, revised=""):
    """블록 → 청크 목록 (id 는 '{file}#{n}', n 은 1부터)."""
    lines = _lines(blocks)
    if any(ARTICLE_RE.match(l["text"]) for l in lines):
        mode = "article"
    elif any(_is_heading(l) for l in lines):
        mode = "heading"
    else:
        chunks = _line_chunks(lines, doc, file, revised)
        for n, c in enumerate(chunks, start=1):
            c["id"] = f"{file}#{n}"
        return chunks

    chapter = ""
    cur = {"chapter": "", "article": "머리말", "page": None, "lines": []}
    sections = [cur]
    for l in lines:
        t = l["text"]
        if m := CHAPTER_RE.match(t):
            chapter = _norm_chapter(m)   # 장 제목 줄은 본문에 넣지 않는다
            cur = {"chapter": chapter, "article": "", "page": l["page"], "lines": []}
            sections.append(cur)
        elif mode == "article" and (m := ARTICLE_RE.match(t)):
            cur = {"chapter": chapter, "article": _norm_article(m), "page": l["page"], "lines": []}
            rest = t[m.end():].strip()   # 제목 뒤에 이어진 본문
            if rest:
                cur["lines"].append(_block(rest, l["page"]))
            sections.append(cur)
        elif mode == "heading" and _is_heading(l):
            cur = {"chapter": chapter, "article": " ".join(t.split()), "page": l["page"], "lines": []}
            sections.append(cur)
        else:
            cur["lines"].append(l)

    chunks = [c for sec in sections for c in _section_chunks(sec, doc, file, revised)]
    for n, c in enumerate(chunks, start=1):
        c["id"] = f"{file}#{n}"
    return chunks


# ── 진입점 ───────────────────────────────────────────────
def parse_file(path, doc=None, revised=None):
    """파일 하나 → 청크 목록. 확장자로 docx / pdf / 텍스트를 구분.
    doc: 문서명(없으면 파일명), revised: 개정일(없으면 본문에서 찾음) — 자료 관리(_meta.json) 값을 넣을 때 사용."""
    fname = os.path.basename(path)
    stem, ext = os.path.splitext(fname)
    doc, ext = doc or stem, ext[1:].lower()
    if ext == 'docx':
        blocks = docx_blocks(path)
    elif ext == 'pdf':
        blocks = pdf_blocks(path)
    elif ext in TEXT_EXTS:
        blocks = text_blocks(path)
    else:
        raise ValueError(f"지원하지 않는 형식: .{ext}")
    return split_articles(blocks, doc, fname, revised or find_revised(blocks))
