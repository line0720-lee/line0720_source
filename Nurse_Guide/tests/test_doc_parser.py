"""doc_parser 단위 테스트 — 표준 라이브러리 unittest 만 사용.
실행(저장소 루트): python -m unittest discover -s Nurse_Guide/tests -v
"""
import os
import re
import sys
import unittest
from unittest import mock

NG_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, NG_DIR)

import doc_parser as dp  # noqa: E402

ARTICLE_START = re.compile(r'^제\s*\d+\s*조(?:의\s*\d+)?\s*[\(（][^\)）]*[\)）]')
CHUNK_KEYS = {"id", "file", "doc", "chapter", "article", "part",
              "page_start", "page_end", "revised", "text", "embed_text"}


# ── 테스트용 블록 헬퍼 ──────────────────────────────────
def B(text, page=None, heading=False):
    return {"text": text, "page": page, "heading": heading}

def blocks_of(lines, page=None):
    return [B(l, page) for l in lines]

def split(blocks, doc="문서", file="문서.docx"):
    return dp.split_articles(blocks, doc, file)


class TestArticleSplit(unittest.TestCase):
    def setUp(self):
        self.blocks = blocks_of([
            "가상 투약관리지침",
            "개정일: 2026-01-01",
            "제1장 총칙",
            "제1조(목적) 이 지침은 투약 안전을 위한 기준을 정한다.",
            "제2조(정의)",
            "① \"투약\"이란 약을 환자에게 주는 행위를 말한다.",
            "② \"확인\"이란 5R을 점검하는 것을 말한다.",
            "제 2 장  투약 절차",
            "제3조(투약 확인)",
            "① 간호사는 투약 전 환자 이름을 확인한다.",
            "제3조의2 (고위험 약물)",
            "① 고위험 약물은 2인이 확인한다.",
        ])
        self.chunks = split(self.blocks)

    def arts(self):
        return [c for c in self.chunks if c["article"] != "머리말"]

    def test_article_boundaries(self):
        self.assertEqual([c["article"] for c in self.arts()],
                         ["제1조(목적)", "제2조(정의)", "제3조(투약 확인)", "제3조의2(고위험 약물)"])

    def test_chapter_carried_to_following_articles(self):
        self.assertEqual([c["chapter"] for c in self.arts()],
                         ["제1장 총칙", "제1장 총칙", "제2장 투약 절차", "제2장 투약 절차"])

    def test_chapter_line_not_in_body(self):
        for c in self.chunks:
            for line in c["text"].split("\n"):
                self.assertFalse(re.match(r'^제\s*\d+\s*장', line), line)

    def test_title_line_trailing_text_starts_body(self):
        first = self.arts()[0]
        self.assertEqual(first["text"], "이 지침은 투약 안전을 위한 기준을 정한다.")

    def test_body_lines_joined(self):
        c = self.arts()[1]
        self.assertEqual(c["text"].split("\n"), [
            "① \"투약\"이란 약을 환자에게 주는 행위를 말한다.",
            "② \"확인\"이란 5R을 점검하는 것을 말한다.",
        ])

    def test_no_merged_articles(self):
        for c in self.arts():
            for line in c["text"].split("\n"):
                self.assertIsNone(ARTICLE_START.match(line), line)

    def test_short_preamble_dropped(self):
        # 머리말 텍스트가 20자 미만이면 청크를 만들지 않는다
        chunks = split(blocks_of(["투약지침", "개정 2026.1.1", "제1조(목적) 목적을 정한다."]))
        self.assertEqual([c["article"] for c in chunks], ["제1조(목적)"])

    def test_preamble_keeps_current_chapter(self):
        pre = [c for c in self.chunks if c["article"] == "머리말"]
        self.assertEqual(len(pre), 1)   # "가상 투약관리지침\n개정일: 2026-01-01" = 26자
        self.assertEqual(pre[0]["chapter"], "")

    def test_long_preamble_kept(self):
        blocks = blocks_of(["가상 지침", "이 문서는 병동 간호사의 투약 업무 전반에 적용되는 공통 원칙을 설명한다.",
                            "제1조(목적) 목적을 정한다."])
        chunks = split(blocks)
        self.assertEqual(chunks[0]["article"], "머리말")
        self.assertIn("공통 원칙", chunks[0]["text"])

    def test_part_empty_and_no_lines_key(self):
        for c in self.arts():
            self.assertEqual(c["part"], "")
            self.assertNotIn("lines", c)


class TestHeadingSplit(unittest.TestCase):
    def test_numbered_headings(self):
        blocks = blocks_of([
            "1. 목적",
            "감염 전파를 예방한다.",
            "2. 손위생",
            "환자 접촉 전후 손위생을 한다.",
            "알코올 손소독제를 사용한다.",
            "가. 장갑 착용",
            "혈액 노출 시 장갑을 착용한다.",
        ])
        chunks = split(blocks)
        self.assertEqual([c["article"] for c in chunks], ["1. 목적", "2. 손위생", "가. 장갑 착용"])
        self.assertEqual(chunks[1]["text"], "환자 접촉 전후 손위생을 한다.\n알코올 손소독제를 사용한다.")

    def test_only_hangul_list_markers_are_headings(self):
        # '가.~하.' 목록 기호만 제목 — '단. ', '즉) ' 같은 일반 문장은 제목이 아니다
        self.assertTrue(dp.HEADING_RE.match("다. 보관"))
        self.assertFalse(dp.HEADING_RE.match("단. 예외는 없다"))
        self.assertFalse(dp.HEADING_RE.match("즉) 다음과 같다"))

    def test_heading_style_blocks(self):
        blocks = [B("개요", heading=True), B("본 지침의 개요."),
                  B("적용 범위", heading=True), B("전 병동에 적용한다.")]
        chunks = split(blocks)
        self.assertEqual([c["article"] for c in chunks], ["개요", "적용 범위"])
        self.assertEqual(chunks[1]["text"], "전 병동에 적용한다.")


class TestLineFallback(unittest.TestCase):
    def test_70_lines(self):
        blocks = blocks_of([f"일반 문장 {i}번 줄입니다 그리고 조금 더 길게 씁니다" for i in range(1, 71)])
        chunks = split(blocks)
        self.assertEqual([c["lines"] for c in chunks], ["1~30", "26~55", "51~70"])
        for c in chunks:
            self.assertEqual(c["article"], "")
            self.assertEqual(c["part"], "")
        self.assertEqual(len(chunks[0]["text"].split("\n")), 30)
        self.assertTrue(chunks[2]["text"].endswith("70번 줄입니다 그리고 조금 더 길게 씁니다"))

    def test_short_doc_single_chunk(self):
        chunks = split(blocks_of(["가나다라마바사 아자차카타파하 문장입니다"] * 5))
        self.assertEqual(len(chunks), 1)
        self.assertEqual(chunks[0]["lines"], "1~5")


class TestFindRevised(unittest.TestCase):
    def test_dash(self):
        self.assertEqual(dp.find_revised(blocks_of(["제목", "개정일: 2026-01-01"])), "2026-01-01")

    def test_dots(self):
        self.assertEqual(dp.find_revised(blocks_of(["시행일 2026. 3. 5."])), "2026-03-05")
        self.assertEqual(dp.find_revised(blocks_of(["(개정 2026.1.1)"])), "2026-01-01")

    def test_korean(self):
        self.assertEqual(dp.find_revised(blocks_of(["개정 2025년 12월 9일"])), "2025-12-09")

    def test_slash(self):
        self.assertEqual(dp.find_revised(blocks_of(["개정일 2026/01/01"])), "2026-01-01")

    def test_none(self):
        self.assertEqual(dp.find_revised(blocks_of(["제목", "작성 2026-01-01"])), "")

    def test_only_first_30_blocks(self):
        blocks = blocks_of(["내용"] * 30 + ["개정일: 2026-01-01"])
        self.assertEqual(dp.find_revised(blocks), "")


class TestChunkFields(unittest.TestCase):
    def test_embed_text_header(self):
        blocks = blocks_of(["제1장 총칙", "제1조(목적) 목적을 정한다."])
        c = dp.split_articles(blocks, "가상_투약관리지침", "가상_투약관리지침.docx")[0]
        self.assertEqual(c["embed_text"], "가상_투약관리지침 > 제1장 총칙 > 제1조(목적)\n목적을 정한다.")
        self.assertEqual(c["id"], "가상_투약관리지침.docx#1")

    def test_embed_text_skips_empty_chapter(self):
        c = split(blocks_of(["제1조(목적) 목적을 정한다."]), doc="D")[0]
        self.assertEqual(c["embed_text"], "D > 제1조(목적)\n목적을 정한다.")

    def test_keys(self):
        for c in split(blocks_of(["제1조(목적) 목적을 정한다.", "제2조(정의) 정의한다."])):
            self.assertEqual(set(c), CHUNK_KEYS)
        for c in split(blocks_of(["평범한 문장 하나 둘 셋 넷 다섯 여섯"] * 3)):
            self.assertEqual(set(c), CHUNK_KEYS | {"lines"})

    def test_pages_span(self):
        blocks = [B("제1조(목적) 목적.", 1), B("① 첫째 항.", 1), B("② 둘째 항.", 2),
                  B("제2조(정의) 정의.", 2), B("① 항.", 3)]
        chunks = split(blocks)
        self.assertEqual([(c["page_start"], c["page_end"]) for c in chunks], [(1, 2), (2, 3)])

    def test_pages_none_for_docx(self):
        c = split(blocks_of(["제1조(목적) 목적을 정한다."]))[0]
        self.assertIsNone(c["page_start"])
        self.assertIsNone(c["page_end"])

    def test_long_article_pieces_get_part_and_pages(self):
        # 사용자 구현과 무관하게 조각 처리(part·페이지)를 검증하려고 분할 함수를 가짜로 바꿔 끼운다
        def fake_split(text, limit=1500):
            lines = text.split("\n")
            return ["\n".join(lines[:2]), "\n".join(lines[2:])]
        blocks = [B("제1조(목적)", 1), B("① " + "가" * 800, 1), B("② " + "나" * 800, 2),
                  B("③ " + "다" * 800, 3)]
        with mock.patch.object(dp, "split_long_article", side_effect=fake_split):
            chunks = split(blocks)
        self.assertEqual([c["part"] for c in chunks], ["1/2", "2/2"])
        self.assertEqual([c["article"] for c in chunks], ["제1조(목적)"] * 2)
        self.assertEqual([(c["page_start"], c["page_end"]) for c in chunks], [(1, 2), (3, 3)])
        self.assertTrue(chunks[0]["embed_text"].startswith("문서 > 제1조(목적) (1/2)\n"))

    def test_page_number_lines_regex(self):
        for s in ["- 2 -", "2", " – 12 – ", "—3—"]:
            self.assertTrue(dp.PAGE_NUM_RE.match(s), s)
        for s in ["제2조", "2. 손위생", "2026-01-01"]:
            self.assertFalse(dp.PAGE_NUM_RE.match(s), s)


class TestSplitLongArticle(unittest.TestCase):
    """split_long_article 은 사용자가 직접 구현하는 함수(학습 모드).
    doc_parser.py 의 TODO(사용자 작성)를 채우기 전까지 이 클래스의 테스트 일부는 실패하는 것이 정상이다."""

    @staticmethod
    def make(sizes, head=None):
        circ = "①②③④⑤⑥⑦⑧⑨⑩"
        lines = [head] if head else []
        lines += [f"{circ[i]} " + "가" * (n - 2) for i, n in enumerate(sizes)]
        return "\n".join(lines)

    def test_short_returns_whole(self):
        t = self.make([100, 100])
        self.assertEqual(dp.split_long_article(t, 1500), [t])

    def test_greedy_packing(self):
        t = self.make([600, 600, 600, 600])
        pieces = dp.split_long_article(t, 1500)
        self.assertEqual(len(pieces), 2)
        self.assertTrue(pieces[0].startswith("①") and "②" in pieces[0])
        self.assertTrue(pieces[1].startswith("③") and "④" in pieces[1])
        for p in pieces:
            self.assertLessEqual(len(p), 1500)

    def test_oversized_clause_alone(self):
        t = self.make([200, 2000, 200])
        pieces = dp.split_long_article(t, 1500)
        self.assertEqual(len(pieces), 3)
        self.assertTrue(pieces[1].startswith("②"))
        self.assertGreater(len(pieces[1]), 1500)

    def test_no_mid_line_split_and_lossless(self):
        t = self.make([700, 500, 900, 300], head="간호사는 다음 각 항을 따른다.")
        pieces = dp.split_long_article(t, 1500)
        self.assertGreater(len(pieces), 1)
        self.assertEqual("\n".join(pieces), t)
        self.assertTrue(pieces[0].startswith("간호사는"))
        orig = set(t.split("\n"))
        for p in pieces:
            for line in p.split("\n"):
                self.assertIn(line, orig)

    def test_sub_items_stay_with_clause(self):
        lines = ["① " + "가" * 1000, "1. 첫째 호", "2. 둘째 호", "② " + "나" * 1000]
        pieces = dp.split_long_article("\n".join(lines), 1500)
        self.assertEqual(pieces, ["\n".join(lines[:3]), lines[3]])


class TestFalsePositives(unittest.TestCase):
    """본문 속 '제N장에서…', '제N조(…)에 따른다' 같은 참조 문장은 장·조 제목이 아니다."""

    def test_chapter_reference_line_is_body(self):
        chunks = split(blocks_of(["제1장 총칙", "제1조(목적) 이 지침의 적용은", "제2장에서 정한 바에 따른다.",
                                  "제2조(정의) 정의."]))
        self.assertEqual([c["article"] for c in chunks], ["제1조(목적)", "제2조(정의)"])
        self.assertIn("제2장에서 정한 바에 따른다.", chunks[0]["text"])
        self.assertEqual(chunks[1]["chapter"], "제1장 총칙")

    def test_chapter_without_title(self):
        chunks = split(blocks_of(["제3장", "제1조(목적) 목적을 정한다."]))
        self.assertEqual(chunks[0]["chapter"], "제3장")

    def test_article_reference_line_is_body(self):
        chunks = split(blocks_of(["제1조(목적) 목적을 정한다.", "제7조(희석 기준)에 따른다.",
                                  "제2조(정의) 정의."]))
        self.assertEqual([c["article"] for c in chunks], ["제1조(목적)", "제2조(정의)"])
        self.assertIn("제7조(희석 기준)에 따른다.", chunks[0]["text"])

    def test_article_title_with_space_body_kept(self):
        self.assertTrue(dp.ARTICLE_RE.match("제1조(목적) 이 지침은 기준을 정한다."))
        self.assertTrue(dp.ARTICLE_RE.match("제2조(정의)"))
        for s in ["제7조(희석 기준)에 따른다.", "제3조(투약)의 규정", "제4조(보관)를 준용한다."]:
            self.assertIsNone(dp.ARTICLE_RE.match(s), s)


class TestPageLineFilter(unittest.TestCase):
    def test_dash_markers_always_dropped(self):
        self.assertEqual(dp._filter_page_lines(["본문", "- 2 -", "표 값", "– 12 –"]), ["본문", "표 값"])

    def test_bare_number_only_at_page_edges(self):
        lines = ["3", "약품명", "100", "2026", "용량", "12"]
        self.assertEqual(dp._filter_page_lines(lines), ["약품명", "100", "2026", "용량"])

    def test_table_cell_numbers_kept(self):
        lines = ["희석 기준표", "약품", "농도", "A약", "100", "B약", "3", "끝"]
        self.assertEqual(dp._filter_page_lines(lines), lines)

    def test_long_number_kept(self):
        self.assertEqual(dp._filter_page_lines(["12345", "본문"]), ["12345", "본문"])


class TestFindRevisedLatest(unittest.TestCase):
    def test_latest_in_history_row(self):
        self.assertEqual(dp.find_revised(blocks_of(["개정이력 | 2019.01.01 | 2026.01.01"])), "2026-01-01")

    def test_latest_across_blocks(self):
        self.assertEqual(dp.find_revised(blocks_of(["제정 2018.01.01", "개정 2024.05.01",
                                                    "개정 2025.03.02"])), "2025-03-02")

    def test_words_between_keyword_and_date(self):
        self.assertEqual(dp.find_revised(blocks_of(["개정 제3차 2026.01.01"])), "2026-01-01")

    def test_invalid_date_rejected(self):
        self.assertEqual(dp.find_revised(blocks_of(["개정일: 2026-02-31"])), "")

    def test_unrelated_later_date_not_chained(self):
        self.assertEqual(dp.find_revised(blocks_of(["시행일 2026.01.01 작성일 2027.01.01"])), "2026-01-01")


class TestParseFileOverrides(unittest.TestCase):
    def test_doc_and_revised_override(self):
        import tempfile
        from docx import Document
        with tempfile.TemporaryDirectory() as d:
            path = os.path.join(d, "abc.docx")
            doc = Document()
            for t in ["개정일: 2020-01-01", "제1조(목적) 목적을 정한다."]:
                doc.add_paragraph(t)
            doc.save(path)
            c = dp.parse_file(path)[0]
            self.assertEqual((c["doc"], c["revised"]), ("abc", "2020-01-01"))
            c = dp.parse_file(path, doc="투약지침", revised="2026-05-05")[0]
            self.assertEqual((c["doc"], c["revised"], c["file"]), ("투약지침", "2026-05-05", "abc.docx"))
            self.assertTrue(c["embed_text"].startswith("투약지침 > 제1조(목적)\n"))
            self.assertEqual(dp.parse_file(path, doc="", revised="")[0]["doc"], "abc")


DATA_DIR = os.path.join(NG_DIR, "Data")
DOCX = os.path.join(DATA_DIR, "가상_투약관리지침.docx")
PDF  = os.path.join(DATA_DIR, "가상_감염관리규정.pdf")


class TestSampleFiles(unittest.TestCase):
    """dev-team-A 가 만든 샘플 파일이 있을 때만 실행."""

    def check_common(self, chunks):
        self.assertTrue(chunks)
        for c in chunks:
            self.assertEqual(c["revised"], "2026-01-01", c["id"])
            if c["article"].startswith("제"):
                for line in c["text"].split("\n"):
                    self.assertIsNone(ARTICLE_START.match(line), f"{c['id']}: {line}")

    @unittest.skipUnless(os.path.exists(DOCX), "샘플 docx 없음")
    def test_docx(self):
        chunks = dp.parse_file(DOCX)
        self.check_common(chunks)
        self.assertTrue(any(" | " in c["text"] for c in chunks), "표 행(' | ')이 청크에 없음")
        self.assertTrue(all(c["doc"] == "가상_투약관리지침" for c in chunks))

    @unittest.skipUnless(os.path.exists(PDF), "샘플 pdf 없음")
    def test_pdf(self):
        chunks = dp.parse_file(PDF)
        self.check_common(chunks)
        self.assertTrue(all(isinstance(c["page_start"], int) for c in chunks))
        self.assertTrue(any(c["page_start"] != c["page_end"] for c in chunks), "페이지를 넘는 청크 없음")


if __name__ == "__main__":
    unittest.main()
