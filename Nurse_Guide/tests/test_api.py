"""backend API 테스트 — 표준 라이브러리 unittest + fastapi.testclient 만 사용.
실행(저장소 루트): python -m unittest discover -s Nurse_Guide/tests -v
실제 Nurse_Guide/Data 는 건드리지 않는다: 임시 폴더에 샘플 2개를 복사해 NURSE_DATA_DIR 로 지정한 뒤 backend 를 import.
"""
import io
import json
import logging
import os
import shutil
import sys
import tempfile
import time
import unittest
import zipfile
from unittest import mock

NG_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REAL_DATA = os.path.join(NG_DIR, "Data")
SAMPLES = ["가상_감염관리규정.pdf", "가상_투약관리지침.docx"]


def _listing(d):
    return sorted(os.path.relpath(os.path.join(r, f), d) for r, ds, fs in os.walk(d) for f in fs + ds)


REAL_BEFORE = _listing(REAL_DATA)
TMP = tempfile.TemporaryDirectory()
for _f in SAMPLES:
    shutil.copy2(os.path.join(REAL_DATA, _f), TMP.name)
os.environ["NURSE_DATA_DIR"] = TMP.name
os.environ["NURSE_ALLOWED_HOSTS"] = "testserver"   # TestClient 의 기본 Host
os.environ["NURSE_GATEWAY"] = ""                    # 실제 게이트웨이로 나가지 않게 (게이트웨이 미설정 상태로 시험)
sys.path.insert(0, NG_DIR)

import backend  # noqa: E402
import httpx  # noqa: E402
from fastapi.testclient import TestClient  # noqa: E402

logging.getLogger("nurse_guide").setLevel(logging.CRITICAL)   # 의도된 오류 로그는 출력하지 않음

PW = "test-pw"
AUTH = {"X-Admin-Password": PW}
with open(os.path.join(REAL_DATA, "가상_투약관리지침.docx"), "rb") as _f:
    DOCX_BYTES = _f.read()
with open(os.path.join(REAL_DATA, "가상_감염관리규정.pdf"), "rb") as _f:
    PDF_BYTES = _f.read()


def tearDownModule():
    TMP.cleanup()
    after = _listing(REAL_DATA)
    if after != REAL_BEFORE:
        raise AssertionError(f"실제 Data 폴더가 바뀌었습니다: {REAL_BEFORE} → {after}")


class ApiCase(unittest.TestCase):
    def setUp(self):
        # 매 테스트마다 임시 Data 를 샘플 2개만 있는 상태로 되돌린다
        for f in os.listdir(TMP.name):
            if f not in SAMPLES:
                os.remove(os.path.join(TMP.name, f))
        for f in SAMPLES:
            if not os.path.exists(os.path.join(TMP.name, f)):
                shutil.copy2(os.path.join(REAL_DATA, f), TMP.name)
        backend._PARSE_CACHE.clear()
        for p in (mock.patch.dict(os.environ, {"NURSE_ADMIN_PASSWORD": PW}),
                  mock.patch.object(backend, "AUTH_FAIL_DELAY", 0)):   # 인증 실패 지연은 TestHardening 에서만 확인
            p.start()
            self.addCleanup(p.stop)
        self.c = TestClient(backend.app)

    # ── 헬퍼 ──
    def docs(self):
        r = self.c.get("/api/docs")
        self.assertEqual(r.status_code, 200)
        return r.json()

    def doc_by_title(self, title):
        return next(d for d in self.docs()["docs"] if d["title"] == title)

    def put_cats(self, cats, headers=AUTH):
        return self.c.put("/api/categories", json={"categories": cats}, headers=headers)

    def upload(self, name="새지침.docx", data=DOCX_BYTES, headers=AUTH, **fields):
        fields.setdefault("author", "홍길동")
        return self.c.post("/api/docs/upload", files={"file": (name, data)}, data=fields, headers=headers)

    def meta(self):
        with open(os.path.join(TMP.name, "_meta.json"), encoding="utf-8") as f:
            return json.load(f)


class TestIndexAndLoad(ApiCase):
    def test_index_served(self):
        r = self.c.get("/")
        self.assertEqual(r.status_code, 200)
        self.assertIn("text/html", r.headers["content-type"])
        self.assertEqual(r.headers["cache-control"], "no-cache")

    def test_load_data_shape(self):
        j = self.c.get("/api/load-data").json()
        self.assertEqual(set(j), {"files", "chunks"})
        self.assertEqual(sorted(f["filename"] for f in j["files"]), sorted(SAMPLES))
        for f in j["files"]:
            self.assertGreater(f["chunks"], 0)
            self.assertEqual(f["doc"], os.path.splitext(f["filename"])[0])
        self.assertTrue(j["chunks"])
        for ch in j["chunks"]:
            self.assertEqual(ch["category"], "미분류")
            self.assertEqual(ch["revised"], "2026-01-01")

    def test_meta_and_temp_files_ignored(self):
        self.put_cats([{"id": None, "name": "투약"}])
        open(os.path.join(TMP.name, "~$temp.docx"), "wb").close()
        names = [f["filename"] for f in self.c.get("/api/load-data").json()["files"]]
        self.assertEqual(sorted(names), sorted(SAMPLES))

    def test_no_server_paths_in_responses(self):
        for url in ["/api/load-data", "/api/docs", "/api/categories"]:
            body = self.c.get(url).text
            self.assertNotIn(TMP.name.replace("\\", "\\\\"), body)
            self.assertNotIn(NG_DIR.replace("\\", "\\\\"), body)

    def test_gateway_missing_503(self):
        self.assertEqual(self.c.post("/api/embed", json={"texts": ["테스트"]}).status_code, 503)
        r = self.c.post("/api/chat", json={"question": "q", "context": "c"})
        self.assertEqual(r.status_code, 503)
        self.assertIn("error", r.json())

    def test_removed_routes(self):
        self.assertEqual(self.c.post("/api/upload-image").status_code, 404)
        self.assertEqual(self.c.post("/api/parse-pdf").status_code, 404)
        self.assertEqual(self.c.get("/images/x.png").status_code, 404)


class TestCategories(ApiCase):
    def test_put_get_order_and_ids(self):
        r = self.put_cats([{"id": None, "name": " 투약 "}, {"id": None, "name": "감염관리"}])
        self.assertEqual(r.status_code, 200)
        cats = r.json()["categories"]
        self.assertEqual([c["name"] for c in cats], ["투약", "감염관리"])
        for c in cats:
            self.assertRegex(c["id"], r"^c[0-9a-f]{12}$")
        self.assertEqual(self.c.get("/api/categories").json()["categories"], cats)
        # 순서 바꾸기·이름 수정: id 유지
        r = self.put_cats([{"id": cats[1]["id"], "name": "감염"}, {"id": cats[0]["id"], "name": "투약"}])
        self.assertEqual([(c["id"], c["name"]) for c in r.json()["categories"]],
                         [(cats[1]["id"], "감염"), (cats[0]["id"], "투약")])

    def test_validation(self):
        self.assertEqual(self.put_cats([{"id": None, "name": "  "}]).status_code, 400)
        self.assertEqual(self.put_cats([{"id": None, "name": "가" * 31}]).status_code, 400)
        self.assertEqual(self.put_cats([{"id": None, "name": "A"}, {"id": None, "name": "a"}]).status_code, 400)
        self.assertEqual(self.put_cats([{"id": "c" + "0" * 12, "name": "x"}]).status_code, 400)
        self.assertEqual(self.put_cats([{"id": None, "name": f"분류{i}"} for i in range(100)]).status_code, 400)
        self.assertEqual(self.put_cats([{"id": None, "name": f"분류{i}"} for i in range(99)]).status_code, 200)
        r = self.c.put("/api/categories", json={"wrong": 1}, headers=AUTH)
        self.assertEqual(r.status_code, 400)
        self.assertIn("error", r.json())

    def test_cannot_delete_used_category(self):
        cid = self.put_cats([{"id": None, "name": "투약"}]).json()["categories"][0]["id"]
        doc = self.doc_by_title("가상_투약관리지침")
        self.c.put(f"/api/docs/{doc['id']}", json={"category": cid, "title": doc["title"], "revised": "",
                                                    "author": "관리자"}, headers=AUTH)
        r = self.put_cats([])
        self.assertEqual(r.status_code, 409)
        self.assertEqual(r.json()["error"], "'투약' 분류에 자료 1개가 있어 삭제할 수 없습니다.")
        self.assertEqual(len(self.c.get("/api/categories").json()["categories"]), 1)

    def test_corrupt_meta(self):
        with open(os.path.join(TMP.name, "_meta.json"), "w", encoding="utf-8") as f:
            f.write("{깨진")
        self.assertEqual(self.c.get("/api/categories").json(), {"categories": []})
        self.assertEqual(len(self.c.get("/api/load-data").json()["files"]), 2)
        r = self.put_cats([{"id": None, "name": "투약"}])
        self.assertEqual(r.status_code, 500)
        with open(os.path.join(TMP.name, "_meta.json"), encoding="utf-8") as f:
            self.assertEqual(f.read(), "{깨진")   # 덮어쓰지 않음


class TestDocs(ApiCase):
    def test_list_untracked(self):
        j = self.docs()
        self.assertEqual(len(j["docs"]), 2)
        for d in j["docs"]:
            self.assertRegex(d["id"], r"^d[0-9a-f]{12}$")
            self.assertEqual((d["category"], d["category_name"], d["author"]), ("", "미분류", ""))
            self.assertEqual(d["revised"], "2026-01-01")
            self.assertGreater(d["chunks"], 0)
            self.assertNotIn("error", d)
        self.assertFalse(os.path.exists(os.path.join(TMP.name, "_meta.json")))   # GET 은 저장하지 않음
        self.assertEqual([d["id"] for d in self.docs()["docs"]], [d["id"] for d in j["docs"]])   # id 고정

    def test_upload_ok(self):
        cid = self.put_cats([{"id": None, "name": "투약"}]).json()["categories"][0]["id"]
        r = self.upload(category=cid, title="새 투약지침", revised="2026-03-01", author=" 김간호 ")
        self.assertEqual(r.status_code, 200, r.text)
        d = r.json()["doc"]
        self.assertEqual((d["title"], d["category"], d["category_name"], d["revised"], d["author"], d["ext"]),
                         ("새 투약지침", cid, "투약", "2026-03-01", "김간호", "docx"))
        self.assertRegex(d["filename"], r"^[0-9a-f]{32}\.docx$")
        self.assertGreater(d["chunks"], 0)
        self.assertTrue(os.path.exists(os.path.join(TMP.name, d["filename"])))
        self.assertEqual(self.meta()["docs"][d["filename"]]["id"], d["id"])
        self.assertEqual(len(self.docs()["docs"]), 3)

    def test_upload_default_title_is_stem(self):
        r = self.upload(name="손위생 지침.pdf", data=PDF_BYTES)
        self.assertEqual(r.status_code, 200, r.text)
        self.assertEqual(r.json()["doc"]["title"], "손위생 지침")
        self.assertEqual(r.json()["doc"]["revised"], "2026-01-01")   # 입력 없으면 본문에서

    def test_upload_rejects(self):
        cases = [
            (dict(name="a.txt", data=b"hello"), 400, "PDF·DOCX 파일만 등록할 수 있습니다."),
            (dict(name="a.pdf", data=b"hello"), 400, "파일 내용이 확장자와 맞지 않습니다."),
            (dict(name="a.docx", data=PDF_BYTES), 400, "파일 내용이 확장자와 맞지 않습니다."),
            (dict(name="a.pdf", data=b"%PDF-1.4 garbage"), 400, None),
            (dict(name="a.docx", author=""), 400, None),
            (dict(name="a.docx", author="  "), 400, None),
            (dict(name="a.docx", author="가" * 21), 400, None),
            (dict(name="a.docx", revised="2026/01/01"), 400, None),
            (dict(name="a.docx", revised="2026-02-31"), 400, None),
            (dict(name="a.docx", category="c" + "1" * 12), 400, None),
            (dict(name="a.docx", replace_id="../x"), 400, "잘못된 ID"),
            (dict(name="a.docx", replace_id="d" + "1" * 12), 404, None),
        ]
        for kw, status, msg in cases:
            r = self.upload(**kw)
            self.assertEqual(r.status_code, status, (kw.get("name"), r.text))
            self.assertIn("error", r.json())
            if msg:
                self.assertEqual(r.json()["error"], msg)
        zbuf = io.BytesIO()
        with zipfile.ZipFile(zbuf, "w") as z:
            z.writestr("hello.txt", "x")
        self.assertEqual(self.upload(name="z.docx", data=zbuf.getvalue()).status_code, 400)
        self.assertEqual(sorted(os.listdir(TMP.name)), sorted(SAMPLES + ["_meta.json"])
                         if os.path.exists(os.path.join(TMP.name, "_meta.json")) else sorted(SAMPLES))

    def test_upload_missing_author_field(self):
        r = self.c.post("/api/docs/upload", files={"file": ("a.docx", DOCX_BYTES)}, headers=AUTH)
        self.assertEqual(r.status_code, 400)
        self.assertEqual(r.json()["error"], "등록자를 1~20자로 입력하세요.")

    def test_upload_too_large(self):
        r = self.upload(name="big.pdf", data=b"%PDF-" + bytes(backend.MAX_UPLOAD))
        self.assertEqual(r.status_code, 413)
        self.assertIn("30MB", r.json()["error"])
        self.assertEqual(sorted(os.listdir(TMP.name)), sorted(SAMPLES))

    def test_duplicate_and_replace(self):
        old = self.doc_by_title("가상_투약관리지침")
        r = self.upload(name="가상_투약관리지침.docx")   # 기본 문서명 = 같은 이름
        self.assertEqual(r.status_code, 409)
        self.assertEqual(r.json(), {"error": "같은 문서명이 이미 있습니다.", "existing_id": old["id"]})
        r = self.upload(name="x.docx", title="  가상_투약관리지침 ".upper())   # 대소문자·공백 무시
        self.assertEqual(r.status_code, 409)
        r = self.upload(name="가상_투약관리지침.docx", replace_id=old["id"], revised="2026-06-01")
        self.assertEqual(r.status_code, 200, r.text)
        new = r.json()["doc"]
        self.assertNotEqual(new["id"], old["id"])   # 미등록(sha1) id 는 물려받지 않는다 — 중복 id 방지
        self.assertNotEqual(new["filename"], old["filename"])
        self.assertEqual(new["revised"], "2026-06-01")
        self.assertFalse(os.path.exists(os.path.join(TMP.name, old["filename"])))
        self.assertEqual(len(self.docs()["docs"]), 2)
        # 등록된 자료를 다시 교체하면 id 유지
        r = self.upload(name="v3.docx", title="가상_투약관리지침", replace_id=new["id"])
        self.assertEqual(r.status_code, 200, r.text)
        self.assertEqual(r.json()["doc"]["id"], new["id"])
        self.assertFalse(os.path.exists(os.path.join(TMP.name, new["filename"])))

    def test_edit(self):
        cid = self.put_cats([{"id": None, "name": "감염관리"}]).json()["categories"][0]["id"]
        d = self.doc_by_title("가상_감염관리규정")
        body = {"category": cid, "title": "감염관리규정", "revised": "2026-02-02", "author": "박수간호사"}
        r = self.c.put(f"/api/docs/{d['id']}", json=body, headers=AUTH)
        self.assertEqual(r.status_code, 200, r.text)
        e = r.json()["doc"]
        self.assertEqual((e["id"], e["title"], e["category_name"], e["revised"], e["author"]),
                         (d["id"], "감염관리규정", "감염관리", "2026-02-02", "박수간호사"))
        # 다른 자료와 같은 문서명 → 409, 등록자·문서명 누락 → 400
        other = self.doc_by_title("가상_투약관리지침")
        r = self.c.put(f"/api/docs/{other['id']}", json=dict(body, title="감염관리규정"), headers=AUTH)
        self.assertEqual(r.status_code, 409)
        self.assertEqual(r.json()["existing_id"], d["id"])
        self.assertEqual(self.c.put(f"/api/docs/{d['id']}", json=dict(body, author=""), headers=AUTH).status_code, 400)
        self.assertEqual(self.c.put(f"/api/docs/{d['id']}", json=dict(body, title=""), headers=AUTH).status_code, 400)
        self.assertEqual(self.c.put(f"/api/docs/{d['id']}", json=dict(body, title="가" * 101),
                                    headers=AUTH).status_code, 400)

    def test_meta_merged_into_load_data(self):
        cid = self.put_cats([{"id": None, "name": "투약"}]).json()["categories"][0]["id"]
        d = self.doc_by_title("가상_투약관리지침")
        self.c.put(f"/api/docs/{d['id']}", json={"category": cid, "title": "투약관리지침 v2",
                                                  "revised": "2026-05-05", "author": "관리자"}, headers=AUTH)
        j = self.c.get("/api/load-data").json()
        f = next(x for x in j["files"] if x["filename"] == d["filename"])
        self.assertEqual(f["doc"], "투약관리지침 v2")
        mine = [c for c in j["chunks"] if c["file"] == d["filename"]]
        self.assertTrue(mine)
        for c in mine:
            self.assertEqual((c["doc"], c["category"], c["revised"]), ("투약관리지침 v2", "투약", "2026-05-05"))
            self.assertTrue(c["embed_text"].startswith("투약관리지침 v2"))
        others = [c for c in j["chunks"] if c["file"] != d["filename"]]
        self.assertTrue(all(c["category"] == "미분류" for c in others))

    def test_delete(self):
        d = self.doc_by_title("가상_감염관리규정")
        r = self.c.delete(f"/api/docs/{d['id']}", headers=AUTH)
        self.assertEqual(r.json(), {"ok": True})
        self.assertFalse(os.path.exists(os.path.join(TMP.name, d["filename"])))
        self.assertEqual(self.c.delete(f"/api/docs/{d['id']}", headers=AUTH).status_code, 404)
        up = self.upload().json()["doc"]
        self.assertEqual(self.c.delete(f"/api/docs/{up['id']}", headers=AUTH).status_code, 200)
        self.assertNotIn(up["filename"], self.meta()["docs"])

    def test_bad_and_unknown_ids(self):
        body = {"category": "", "title": "t", "revised": "", "author": "a"}
        for bad in ["..%2Fx", "..%2F..%2Fbackend.py", "%2E%2E%5Cx", "x", "D" + "0" * 12, "d" + "0" * 11]:
            for r in (self.c.delete(f"/api/docs/{bad}", headers=AUTH),
                      self.c.put(f"/api/docs/{bad}", json=body, headers=AUTH)):
                self.assertEqual(r.status_code, 400, (bad, r.text))
                self.assertEqual(r.json(), {"error": "잘못된 ID"})
        r = self.c.request("DELETE", "/api/docs/../x", headers=AUTH)
        self.assertIn(r.status_code, (400, 404))   # httpx 가 '..' 를 정리해 /api/x(404)로 보낸다 — 어느 쪽이든 삭제 안 됨
        self.assertIn("error", r.json())
        self.assertEqual(self.c.get("/api/nope").json(), {"error": "요청한 주소를 찾을 수 없습니다."})
        for unknown in ["d" + "0" * 12, "c" + "0" * 12]:
            self.assertEqual(self.c.delete(f"/api/docs/{unknown}", headers=AUTH).status_code, 404)
            self.assertEqual(self.c.put(f"/api/docs/{unknown}", json=body, headers=AUTH).status_code, 404)
        self.assertEqual(sorted(os.listdir(TMP.name)), sorted(SAMPLES))


class TestAuth(ApiCase):
    def write_calls(self, headers):
        d_id = "d" + "0" * 12
        body = {"category": "", "title": "t", "revised": "", "author": "a"}
        return [
            self.put_cats([{"id": None, "name": "x"}], headers=headers),
            self.upload(headers=headers),
            self.c.put(f"/api/docs/{d_id}", json=body, headers=headers),
            self.c.delete(f"/api/docs/{d_id}", headers=headers),
        ]

    def test_no_header_401(self):
        for r in self.write_calls({}):
            self.assertEqual(r.status_code, 401)
            self.assertEqual(r.json(), {"error": "관리자 인증이 필요합니다."})

    def test_wrong_password_401(self):
        for r in self.write_calls({"X-Admin-Password": "wrong"}):
            self.assertEqual(r.status_code, 401)

    def test_env_unset_503(self):
        with mock.patch.dict(os.environ, {"NURSE_ADMIN_PASSWORD": ""}):
            for r in self.write_calls(AUTH):
                self.assertEqual(r.status_code, 503)
                self.assertEqual(r.json(), {"error": "관리자 비밀번호가 설정되지 않았습니다 (NURSE_ADMIN_PASSWORD)"})
        env = {k: v for k, v in os.environ.items() if k != "NURSE_ADMIN_PASSWORD"}
        with mock.patch.dict(os.environ, env, clear=True):
            self.assertEqual(self.write_calls(AUTH)[0].status_code, 503)

    def test_reads_need_no_auth(self):
        for url in ["/", "/api/load-data", "/api/docs", "/api/categories", "/health"]:
            self.assertEqual(self.c.get(url).status_code, 200, url)

    def test_nothing_written_without_auth(self):
        self.write_calls({})
        self.assertEqual(sorted(os.listdir(TMP.name)), sorted(SAMPLES))

    def test_cors(self):
        r = self.c.options("/api/categories", headers={"Origin": "http://localhost:3003",
                                                       "Access-Control-Request-Method": "PUT",
                                                       "Access-Control-Request-Headers": "X-Admin-Password"})
        self.assertEqual(r.headers.get("access-control-allow-origin"), "http://localhost:3003")
        r = self.c.get("/api/categories", headers={"Origin": "http://evil.example"})
        self.assertNotIn("access-control-allow-origin", r.headers)


class TestHardening(ApiCase):
    def gateway(self, handler):
        """게이트웨이를 가짜 전송(MockTransport)으로 바꿔 끼운다 — 실제 네트워크 호출 없음."""
        real = httpx.AsyncClient
        for p in (mock.patch.multiple(backend, GATEWAY="http://gw.test", OLLAMA_URL="http://gw.test/api/chat",
                                      OLLAMA_EMBED="http://gw.test/api/embed"),
                  mock.patch.object(backend.httpx, "AsyncClient",
                                    lambda **kw: real(transport=httpx.MockTransport(handler), **kw))):
            p.start()
            self.addCleanup(p.stop)

    @staticmethod
    def down(request):
        raise httpx.ConnectError("refused", request=request)

    def test_chunked_upload_411(self):
        r = self.c.post("/api/docs/upload", content=iter([b"abc"]), headers=AUTH)
        self.assertEqual(r.status_code, 411)
        self.assertEqual(r.json(), {"error": "파일 크기 정보(Content-Length)가 없는 요청은 받을 수 없습니다."})

    def test_upload_auth_checked_before_body(self):
        h = {"Content-Type": "multipart/form-data; boundary=xyz"}
        self.assertEqual(self.c.post("/api/docs/upload", content=b"garbage", headers=h).status_code, 401)
        r = self.c.post("/api/docs/upload", content=b"garbage", headers={**h, "X-Admin-Password": "bad"})
        self.assertEqual(r.json(), {"error": "관리자 인증이 필요합니다."})
        with mock.patch.dict(os.environ, {"NURSE_ADMIN_PASSWORD": ""}):
            self.assertEqual(self.c.post("/api/docs/upload", content=b"garbage", headers=h).status_code, 503)
        self.assertEqual(self.c.post("/api/docs/upload", content=b"garbage", headers={**h, **AUTH}).status_code, 400)

    def test_content_length_precheck_413(self):
        r = self.c.post("/api/docs/upload", content=b"x",
                        headers={**AUTH, "Content-Length": str(backend.MAX_UPLOAD + 2 * 1024 * 1024)})
        self.assertEqual(r.status_code, 413)

    def test_failed_password_delay(self):
        with mock.patch.object(backend, "AUTH_FAIL_DELAY", 0.3):
            t = time.monotonic()
            self.assertEqual(self.put_cats([], headers={"X-Admin-Password": "bad"}).status_code, 401)
            self.assertGreaterEqual(time.monotonic() - t, 0.3)
            t = time.monotonic()
            self.assertEqual(self.put_cats([]).status_code, 200)
            with mock.patch.dict(os.environ, {"NURSE_ADMIN_PASSWORD": ""}):
                self.assertEqual(self.put_cats([]).status_code, 503)
            self.assertLess(time.monotonic() - t, 0.3)

    def test_replace_old_file_remove_failure(self):
        old = self.doc_by_title("가상_감염관리규정")
        real_remove = os.remove

        def remove(path, *a, **kw):
            if os.path.basename(path) == old["filename"]:
                raise PermissionError("locked")
            return real_remove(path, *a, **kw)

        with mock.patch.object(backend.os, "remove", remove):
            r = self.upload(name="x.pdf", data=PDF_BYTES, title="가상_감염관리규정", replace_id=old["id"])
        self.assertEqual(r.status_code, 200, r.text)
        ids = [d["id"] for d in self.docs()["docs"]]
        self.assertEqual(len(ids), len(set(ids)))   # 남은 옛 파일과 새 자료의 id 가 겹치지 않음

    def test_write_meta_retries_permission_error(self):
        real_replace, calls = os.replace, []

        def flaky(src, dst):
            calls.append(dst)
            if dst == backend.META_PATH and len(calls) <= 2:
                raise PermissionError("locked")
            return real_replace(src, dst)

        with mock.patch.object(backend.os, "replace", flaky), mock.patch.object(backend.time, "sleep") as sl:
            self.assertEqual(self.put_cats([{"id": None, "name": "투약"}]).status_code, 200)
        self.assertEqual(len(calls), 3)
        self.assertEqual(sl.call_count, 2)
        self.assertEqual(len(self.c.get("/api/categories").json()["categories"]), 1)

    def test_write_meta_gives_up_with_generic_500(self):
        c = TestClient(backend.app, raise_server_exceptions=False)

        def locked(src, dst):
            raise PermissionError(f"locked {dst}")

        with mock.patch.object(backend.os, "replace", locked), mock.patch.object(backend.time, "sleep"):
            r = c.put("/api/categories", json={"categories": []}, headers=AUTH)
        self.assertEqual(r.status_code, 500)
        self.assertEqual(r.json(), {"error": "서버 내부 오류가 발생했습니다."})
        self.assertEqual([f for f in os.listdir(TMP.name) if f.endswith(".tmp")], [])

    def test_embed_gateway_down_502(self):
        self.gateway(self.down)
        r = self.c.post("/api/embed", json={"texts": ["테스트"]})
        self.assertEqual(r.status_code, 502)
        self.assertEqual(r.json(), {"error": "모델 게이트웨이에 연결할 수 없습니다."})

    def test_embed_ok(self):
        self.gateway(lambda req: httpx.Response(200, json={"embeddings": [[0.1, 0.2]]}))
        self.assertEqual(self.c.post("/api/embed", json={"texts": ["테스트"]}).json(), {"embeddings": [[0.1, 0.2]]})

    def test_chat_gateway_down_sse_error(self):
        self.gateway(self.down)
        r = self.c.post("/api/chat", json={"question": "q", "context": "c"})
        self.assertEqual(r.status_code, 200)
        self.assertIn(json.dumps({"token": "⚠️ 모델 게이트웨이에 연결할 수 없습니다."}), r.text)
        self.assertTrue(r.text.rstrip().endswith("data: [DONE]"))
        self.assertNotIn("gw.test", r.text)

    def test_chat_stream_ok(self):
        lines = [{"message": {"content": "안녕"}, "done": False}, {"message": {"content": ""}, "done": True}]
        body = "\n".join(json.dumps(l) for l in lines) + "\n"
        self.gateway(lambda req: httpx.Response(200, text=body))
        r = self.c.post("/api/chat", json={"question": "q", "context": "c"})
        self.assertIn(json.dumps({"token": "안녕"}), r.text)
        self.assertIn("data: [DONE]", r.text)

    def test_id_trailing_newline_rejected(self):
        for r in (self.c.delete("/api/docs/d0123456789ab%0A", headers=AUTH),
                  self.c.put("/api/docs/d0123456789ab%0A", json={"title": "t", "author": "a"}, headers=AUTH)):
            self.assertEqual(r.status_code, 400)
            self.assertEqual(r.json(), {"error": "잘못된 ID"})

    def test_input_limits(self):
        ok = {"question": "q" * 1000, "context": "c" * 20000}
        self.assertEqual(self.c.post("/api/chat", json=ok).status_code, 503)   # 한도 안 → 게이트웨이 미설정
        for bad in [dict(ok, question="q" * 1001), dict(ok, context="c" * 20001)]:
            r = self.c.post("/api/chat", json=bad)
            self.assertEqual(r.status_code, 400)
            self.assertIn("error", r.json())
        self.assertEqual(self.c.post("/api/embed", json={"texts": ["t" * 8000] * 64}).status_code, 503)
        self.assertEqual(self.c.post("/api/embed", json={"texts": ["t"] * 65}).status_code, 400)
        self.assertEqual(self.c.post("/api/embed", json={"texts": ["t" * 8001]}).status_code, 400)


class TestGatewayConfig(unittest.TestCase):
    """게이트웨이 주소는 코드가 아니라 환경변수 NURSE_GATEWAY(.env)에서만 읽는다."""

    def test_reads_env_and_strips_slash(self):
        with mock.patch.dict(os.environ, {"NURSE_GATEWAY": " http://gw.example:11500/ "}):
            self.assertEqual(backend.gateway_urls(),
                             ("http://gw.example:11500", "http://gw.example:11500/api/chat",
                              "http://gw.example:11500/api/embed"))

    def test_unset_means_empty(self):
        env = {k: v for k, v in os.environ.items() if k != "NURSE_GATEWAY"}
        with mock.patch.dict(os.environ, env, clear=True):
            self.assertEqual(backend.gateway_urls()[0], "")

    def test_env_file_sets_gateway(self):
        with tempfile.TemporaryDirectory() as d:
            p = os.path.join(d, ".env")
            with open(p, "w", encoding="utf-8") as f:
                f.write("# 주석\nNURSE_GATEWAY=http://gw.example:11500\n")
            env = {k: v for k, v in os.environ.items() if k != "NURSE_GATEWAY"}
            with mock.patch.dict(os.environ, env, clear=True):
                backend.load_env_file(p)
                self.assertEqual(backend.gateway_urls()[0], "http://gw.example:11500")


class TestHostCheck(ApiCase):
    """DNS 리바인딩 방어 — 허용된 Host 로 들어온 요청만 처리."""

    def test_unknown_host_rejected(self):
        for path in ("/", "/api/docs", "/api/load-data", "/health"):
            r = self.c.get(path, headers={"Host": "evil.example"})
            self.assertEqual(r.status_code, 400, path)
            self.assertIn("error", r.json())

    def test_default_and_configured_hosts(self):
        for host in ("localhost:8001", "127.0.0.1:8001", "[::1]:8001", "testserver"):
            self.assertEqual(self.c.get("/health", headers={"Host": host}).status_code, 200, host)
        with mock.patch.dict(os.environ, {"NURSE_ALLOWED_HOSTS": "nurse-guide.internal, 10.0.0.5"}):
            self.assertEqual(self.c.get("/health", headers={"Host": "nurse-guide.internal:8001"}).status_code, 200)
            self.assertEqual(self.c.get("/health", headers={"Host": "10.0.0.5:8001"}).status_code, 200)
            self.assertEqual(self.c.get("/health", headers={"Host": "testserver"}).status_code, 400)

    def test_wildcard_allows_any(self):
        with mock.patch.dict(os.environ, {"NURSE_ALLOWED_HOSTS": "*"}):
            self.assertEqual(self.c.get("/health", headers={"Host": "anything.example"}).status_code, 200)


if __name__ == "__main__":
    unittest.main()
