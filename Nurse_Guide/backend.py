from fastapi import FastAPI, UploadFile, File, Form, Request, Depends, HTTPException
from fastapi.exceptions import RequestValidationError
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import StreamingResponse, JSONResponse, FileResponse
from starlette.exceptions import HTTPException as StarletteHTTPException
from starlette.convertors import Convertor, register_url_convertor
from pydantic import BaseModel, Field
from typing import Annotated
from datetime import date
import asyncio
import hashlib
import hmac
import httpx
import io
import json
import logging
import os
import re
import tempfile
import time
import uuid
import zipfile

from doc_parser import SUPPORTED_EXTS, parse_file   # 조항 단위 파서

log = logging.getLogger("nurse_guide")

# ── 경로·환경변수 ────────────────────────────────────────
BASE_DIR  = os.path.dirname(os.path.abspath(__file__))
DATA_DIR  = os.environ.get("NURSE_DATA_DIR") or os.path.join(BASE_DIR, "Data")
os.makedirs(DATA_DIR, exist_ok=True)
META_PATH = os.path.join(DATA_DIR, "_meta.json")
PORT      = int(os.environ.get("NURSE_PORT", "8001"))

UPLOAD_EXTS = {"pdf", "docx"}
MAX_UPLOAD  = 30 * 1024 * 1024   # 30MB
MAX_CATS    = 99
AUTH_FAIL_DELAY = 1.0            # 비밀번호가 틀리면 이만큼(초) 늦게 응답 — 무차별 대입 완화

def load_env_file(path=os.path.join(BASE_DIR, ".env")):
    """KEY=VALUE 형식의 .env(깃 제외)를 읽어 환경변수에 없는 값만 채운다. 직접 실행할 때만 호출."""
    if not os.path.isfile(path):
        return
    with open(path, encoding="utf-8") as f:
        for line in f:
            key, sep, value = line.strip().partition("=")
            if sep and key and not key.startswith("#"):
                os.environ.setdefault(key.strip(), value.strip().strip('"').strip("'"))


app = FastAPI()


@app.middleware("http")
async def limit_upload_size(request: Request, call_next):
    """업로드 본문을 받기 전에 ① Content-Length 필수(청크 전송 거절) ② 관리자 인증 ③ 30MB 초과를 먼저 거절한다.
    (FastAPI 는 Depends 보다 multipart 본문을 먼저 읽으므로 여기서 막는다.)"""
    if request.method == "POST" and request.url.path == "/api/docs/upload":
        try:
            length = int(request.headers.get("content-length") or "")
        except ValueError:
            length = None
        if length is None or "transfer-encoding" in request.headers:
            return JSONResponse(status_code=411,
                                content={"error": "파일 크기 정보(Content-Length)가 없는 요청은 받을 수 없습니다."})
        denied = await admin_denied(request)
        if denied:
            return JSONResponse(status_code=denied[0], content={"error": denied[1]})
        if length > MAX_UPLOAD + 1024 * 1024:
            return JSONResponse(status_code=413, content={"error": "파일 크기는 30MB 이하만 등록할 수 있습니다."})
    return await call_next(request)


DEFAULT_HOSTS = {"localhost", "127.0.0.1", "[::1]"}


def allowed_hosts():
    """허용 Host 목록 = 기본(로컬) + NURSE_ALLOWED_HOSTS(쉼표 구분, '*'는 전부 허용). .env 반영을 위해 요청마다 읽는다."""
    extra = {h.strip().lower() for h in os.environ.get("NURSE_ALLOWED_HOSTS", "").split(",") if h.strip()}
    return DEFAULT_HOSTS | extra


@app.middleware("http")
async def check_host(request: Request, call_next):
    """DNS 리바인딩 방어: 허용되지 않은 Host 헤더로 온 요청은 처리하지 않는다."""
    host = (request.headers.get("host") or "").strip().lower()
    name = host.split("]")[0] + "]" if host.startswith("[") else host.split(":")[0]   # 포트 제거 (IPv6 는 [..] 유지)
    hosts = allowed_hosts()
    if "*" not in hosts and name not in hosts:
        return JSONResponse(status_code=400, content={"error": "허용되지 않은 접속 주소입니다 (NURSE_ALLOWED_HOSTS)."})
    return await call_next(request)


app.add_middleware(
    CORSMiddleware,
    allow_origins=["http://localhost:3003", "http://127.0.0.1:3003"],
    allow_methods=["GET", "POST", "PUT", "DELETE"],
    allow_headers=["Content-Type", "X-Admin-Password"],
)

# ── 공유 게이트웨이 주소 (모든 모델 호출은 이 변수만 사용) ──────────────
# 주소는 외부 공유 금지 → 코드·깃에 쓰지 않고 환경변수 NURSE_GATEWAY(.env)로만 받는다. 예: http://게이트웨이:11500
def gateway_urls():
    gw = os.environ.get("NURSE_GATEWAY", "").strip().rstrip("/")
    return gw, f"{gw}/api/chat", f"{gw}/api/embed"


GATEWAY, OLLAMA_URL, OLLAMA_EMBED = gateway_urls()
MODEL        = "qwen3.6:35b-a3b"
EMBED_MODEL  = "bge-m3"

SYSTEM_PROMPT = (
    "당신은 간호부 규정·지침 안내 챗봇입니다. "
    "아래 참고 자료는 간호부 규정·지침 파일에서 질문과 관련성이 높은 조각만 선별(RAG)한 것이며, "
    "각 조각 머리말은 '[구간 N] 문서: 문서명 · 제N조(제목) · p.N · 개정일 YYYY-MM-DD' 형식입니다 "
    "(조항·쪽·개정일은 없을 수 있습니다).\n"
    "규칙 1 (근거 기반): 반드시 제공된 참고 자료에만 근거해 한국어로 답한다. "
    "자료에 없는 내용은 지어내지 말고 '규정에서 확인되지 않습니다'라고만 답한다.\n"
    "규칙 2 (출처 표시): 근거가 된 문장(또는 답변) 끝에 출처 태그를 반드시 단다. "
    "조항이 있으면 조 번호만 써서 [문서명 제N조] (예: [가상_투약관리지침 제5조]), "
    "조항이 없으면 [문서명 p.N], 쪽도 없으면 [문서명 제목] 형식으로 쓴다. "
    "문서명은 머리말의 '문서:' 뒤에 적힌 이름을 그대로 쓴다.\n"
    "규칙 3 (형식): 절차가 여러 단계이면 번호를 붙여 한 줄에 한 단계씩 쓴다.\n"
    "규칙 4 (환자 안전): 투약 용량·희석·주입 속도·응급 처치와 관련된 답변은 "
    "마지막 줄에 '※ 실제 시행 전 원문과 처방을 반드시 확인하세요.'를 붙인다.\n"
    "규칙 5 (개인정보·임상 판단): 질문에 특정 환자의 이름·등록번호·생년월일 같은 개인정보가 들어 있거나, "
    "특정 환자에게 어떤 처치를 해도 되는지 판단을 요청할 때에만 '개인정보·임상 판단은 안내하지 않습니다'라고만 답한다. "
    "환자와 관련 없는 질문(주차·식당·행정 등)이나 자료에 없는 일반 질문에는 이 문구를 쓰지 말고 규칙 1에 따라 "
    "'규정에서 확인되지 않습니다'라고 답한다.\n"
    "규칙 6 (개정일): 같은 내용이 개정일이 다른 문서에 있으면 개정일이 최신인 문서를 우선하고, "
    "문서 간 내용이 다르다는 점을 함께 알려준다.\n"
    "예시:\n"
    "1. 투약 전 환자 이름과 등록번호로 본인을 확인합니다. [가상_투약관리지침 제5조]\n"
    "2. 처방과 약품명·용량·경로·시간을 대조합니다. [가상_투약관리지침 제5조]\n"
    "※ 실제 시행 전 원문과 처방을 반드시 확인하세요."
)


# ── 오류 응답 형식 통일 {"error": "..."} ─────────────────
DEFAULT_ERRORS = {"Not Found": "요청한 주소를 찾을 수 없습니다.", "Method Not Allowed": "허용되지 않는 요청 방식입니다."}


@app.exception_handler(StarletteHTTPException)
async def http_error(request: Request, exc: StarletteHTTPException):
    msg = DEFAULT_ERRORS.get(str(exc.detail), str(exc.detail))
    return JSONResponse(status_code=exc.status_code, content={"error": msg}, headers=getattr(exc, "headers", None))


@app.exception_handler(RequestValidationError)
async def validation_error(request: Request, exc: RequestValidationError):
    return JSONResponse(status_code=400, content={"error": "요청 형식이 올바르지 않습니다."})


@app.exception_handler(Exception)
async def server_error(request: Request, exc: Exception):
    log.exception("처리되지 않은 오류: %s %s", request.method, request.url.path)   # 원문·경로는 로그에만
    return JSONResponse(status_code=500, content={"error": "서버 내부 오류가 발생했습니다."})


def fail(status, msg):
    raise HTTPException(status_code=status, detail=msg)


# ── 관리자 인증 (쓰기 API 전용) ──────────────────────────
async def admin_denied(request: Request):
    """관리자 인증 검사 (require_admin·업로드 미들웨어 공용). 통과면 None, 아니면 (상태코드, 문구)."""
    pw = os.environ.get("NURSE_ADMIN_PASSWORD") or ""
    if not pw:
        return 503, "관리자 비밀번호가 설정되지 않았습니다 (NURSE_ADMIN_PASSWORD)"
    given = request.headers.get("X-Admin-Password") or ""
    if not hmac.compare_digest(given.encode("utf-8"), pw.encode("utf-8")):
        await asyncio.sleep(AUTH_FAIL_DELAY)
        return 401, "관리자 인증이 필요합니다."
    return None


async def require_admin(request: Request):
    denied = await admin_denied(request)
    if denied:
        fail(*denied)


# ── 자료 정보 저장소 (Data/_meta.json) ───────────────────
META_LOCK = asyncio.Lock()   # 모든 쓰기(메타·파일)는 이 잠금 안에서만
ID_RE     = re.compile(r'[cd][0-9a-f]{12}')   # 항상 fullmatch 로 검사 (끝 줄바꿈도 거절)
DATE_RE   = re.compile(r'\d{4}-\d{2}-\d{2}')


class MetaError(Exception):
    pass


def _empty_meta():
    return {"categories": [], "docs": {}}


def read_meta(strict=False):
    """_meta.json 읽기. 없으면 빈 값. 손상 시 읽기는 빈 값(로그), strict(쓰기)면 MetaError."""
    if not os.path.exists(META_PATH):
        return _empty_meta()
    try:
        with open(META_PATH, encoding="utf-8") as f:
            meta = json.load(f)
        if not (isinstance(meta, dict) and isinstance(meta.get("categories", []), list)
                and isinstance(meta.get("docs", {}), dict)
                and all(isinstance(c, dict) and isinstance(c.get("id"), str) and isinstance(c.get("name"), str)
                        for c in meta.get("categories", []))):
            raise ValueError("구조가 올바르지 않음")
        meta.setdefault("categories", [])
        meta.setdefault("docs", {})
        return meta
    except (OSError, ValueError) as e:
        log.error("_meta.json 읽기 실패: %s", e)
        if strict:
            raise MetaError() from e
        return _empty_meta()


def write_meta(meta):
    """같은 폴더 임시 파일에 쓴 뒤 os.replace (원자적 교체)."""
    fd, tmp = tempfile.mkstemp(dir=DATA_DIR, prefix="_meta.", suffix=".tmp")
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as f:
            json.dump(meta, f, ensure_ascii=False, indent=2)
        for attempt in range(5):   # Windows 에서 백신·백업 프로그램이 잠깐 잡고 있으면 PermissionError
            try:
                os.replace(tmp, META_PATH)
                break
            except PermissionError:
                if attempt == 4:
                    raise
                time.sleep(0.1)
    except BaseException:
        if os.path.exists(tmp):
            os.remove(tmp)
        raise


def load_meta_for_write():
    try:
        return read_meta(strict=True)
    except MetaError:
        fail(500, "자료 정보 파일(_meta.json)이 손상되어 저장할 수 없습니다. 관리자에게 문의하세요.")


def new_id(prefix):
    return prefix + uuid.uuid4().hex[:12]


def untracked_id(fname):
    return "d" + hashlib.sha1(fname.encode("utf-8")).hexdigest()[:12]


def doc_files():
    """DATA_DIR 의 문서 파일명 목록 ('_', '~', '.' 로 시작하는 파일·미지원 형식 제외)."""
    out = []
    for fname in sorted(os.listdir(DATA_DIR)):
        if fname.startswith(('_', '~', '.')):   # _meta.json·Word 임시파일·숨김파일 제외
            continue
        if not os.path.isfile(os.path.join(DATA_DIR, fname)):
            continue
        if os.path.splitext(fname)[1][1:].lower() in SUPPORTED_EXTS:
            out.append(fname)
    return out


def doc_entry(meta, fname):
    """메타 항목(없으면 미분류 기본값 — 저장하지 않음)."""
    e = meta["docs"].get(fname)
    if isinstance(e, dict) and ID_RE.fullmatch(str(e.get("id", ""))):
        return {"id": e["id"], "category": str(e.get("category") or ""), "title": str(e.get("title") or ""),
                "revised": str(e.get("revised") or ""), "author": str(e.get("author") or ""),
                "uploaded": str(e.get("uploaded") or "")}
    mtime = os.path.getmtime(os.path.join(DATA_DIR, fname))
    return {"id": untracked_id(fname), "category": "", "title": "", "revised": "", "author": "",
            "uploaded": date.fromtimestamp(mtime).isoformat()}


def find_doc(meta, doc_id):
    """id → 파일명 (없으면 None)."""
    for fname in doc_files():
        if doc_entry(meta, fname)["id"] == doc_id:
            return fname
    return None


class AnyIdConvertor(Convertor):
    """'/'·줄바꿈까지 포함한 어떤 id 도 라우트에 닿게 해서 check_id 가 400 으로 거절하게 한다 (기본 path 는 줄바꿈 불일치 → 404)."""
    regex = r"[\s\S]+"

    def convert(self, value):
        return value

    def to_string(self, value):
        return str(value)


register_url_convertor("anyid", AnyIdConvertor())


def check_id(value):
    if not ID_RE.fullmatch(value or ""):
        fail(400, "잘못된 ID")


# ── 파싱 캐시 (파일명 → (서명, 결과)) ────────────────────
_PARSE_CACHE = {}


def cached_parse(fname, doc, revised):
    """(mtime_ns, size, 문서명, 개정일)이 같으면 다시 파싱하지 않는다. 결과: (청크 목록 | None, 오류 문구)."""
    st = os.stat(os.path.join(DATA_DIR, fname))
    sig = (st.st_mtime_ns, st.st_size, doc, revised)
    hit = _PARSE_CACHE.get(fname)
    if hit and hit[0] == sig:
        return hit[1]
    try:
        res = (parse_file(os.path.join(DATA_DIR, fname), doc=doc, revised=revised or None), "")
    except Exception as e:
        log.warning("파싱 실패 %s: %s", fname, e)
        res = (None, f"문서를 읽을 수 없습니다 ({type(e).__name__})")   # 서버 경로가 담길 수 있어 원문 메시지는 숨김
    _PARSE_CACHE[fname] = (sig, res)
    return res


def _title(meta, fname):
    return doc_entry(meta, fname)["title"] or os.path.splitext(fname)[0]


def doc_view(meta, fname):
    """/api/docs 항목 하나."""
    e = doc_entry(meta, fname)
    title = e["title"] or os.path.splitext(fname)[0]
    cats = {c["id"]: c["name"] for c in meta["categories"]}
    chunks, err = cached_parse(fname, title, e["revised"])
    revised = e["revised"] or (chunks[0]["revised"] if chunks else "")
    v = {"id": e["id"], "filename": fname, "ext": os.path.splitext(fname)[1][1:].lower(), "title": title,
         "category": e["category"] if e["category"] in cats else "",
         "category_name": cats.get(e["category"], "미분류"), "revised": revised, "author": e["author"],
         "uploaded": e["uploaded"], "chunks": None if chunks is None else len(chunks)}
    if err:
        v["error"] = err
    return v


def list_docs(meta):
    order = {c["id"]: i for i, c in enumerate(meta["categories"])}
    docs = [doc_view(meta, f) for f in doc_files()]
    docs.sort(key=lambda d: (order.get(d["category"], len(order)), d["title"].casefold(), d["filename"]))
    return docs


def public_categories(meta):
    return [{"id": c["id"], "name": c["name"]} for c in meta["categories"]]


# ── 입력 검사 ────────────────────────────────────────────
def clean_author(author):
    a = (author or "").strip()
    if not 1 <= len(a) <= 20:
        fail(400, "등록자를 1~20자로 입력하세요.")
    return a


def clean_title(title):
    t = " ".join((title or "").split())
    if not 1 <= len(t) <= 100:
        fail(400, "문서명을 1~100자로 입력하세요.")
    return t


def clean_revised(revised):
    r = (revised or "").strip()
    if not r:
        return ""
    try:
        if not DATE_RE.fullmatch(r):
            raise ValueError
        date.fromisoformat(r)
    except ValueError:
        fail(400, "개정일은 YYYY-MM-DD 형식으로 입력하세요.")
    return r


def check_category(meta, cat):
    cat = (cat or "").strip()
    if cat and cat not in {c["id"] for c in meta["categories"]}:
        fail(400, "존재하지 않는 분류입니다.")
    return cat


def duplicate_of(meta, title, exclude=None):
    """같은 문서명(대소문자·앞뒤 공백 무시)을 가진 다른 자료 id (없으면 None)."""
    key = title.strip().casefold()
    for fname in doc_files():
        if fname != exclude and _title(meta, fname).strip().casefold() == key:
            return doc_entry(meta, fname)["id"]
    return None


def sniff_ok(ext, data):
    """확장자 위조 검사: PDF 는 %PDF- 로 시작, DOCX 는 word/document.xml 이 든 zip."""
    if ext == "pdf":
        return data.startswith(b"%PDF-")
    try:
        with zipfile.ZipFile(io.BytesIO(data)) as z:
            return "word/document.xml" in z.namelist()
    except zipfile.BadZipFile:
        return False


def _try_parse(ext, data, title, revised):
    with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as d:
        path = os.path.join(d, f"upload.{ext}")
        with open(path, "wb") as f:
            f.write(data)
        parse_file(path, doc=title, revised=revised or None)


class ChatRequest(BaseModel):
    question: str = Field(max_length=1000)
    context: str = Field(max_length=20000)

class EmbedRequest(BaseModel):
    texts: list[Annotated[str, Field(max_length=8000)]] = Field(max_length=64)

class CategoryItem(BaseModel):
    id: str | None = None
    name: str = ""

class CategoriesRequest(BaseModel):
    categories: list[CategoryItem]

class DocEditRequest(BaseModel):
    category: str = ""
    title: str = ""
    revised: str = ""
    author: str = ""



async def stream_ollama(question: str, context: str):
    payload = {
        "model": MODEL,
        "stream": True,
        "think": False,
        "messages": [
            {"role": "system", "content": SYSTEM_PROMPT},
            {"role": "user", "content": f"/no_think\n참고 자료:\n{context}\n\n질문: {question}"},
        ],
    }
    try:
        async with httpx.AsyncClient(timeout=120) as client:
            async with client.stream("POST", OLLAMA_URL, json=payload) as resp:
                async for line in resp.aiter_lines():
                    if not line:
                        continue
                    try:
                        data = json.loads(line)
                        token = data.get("message", {}).get("content", "")
                        if token:
                            yield f"data: {json.dumps({'token': token})}\n\n"
                        if data.get("done"):
                            yield "data: [DONE]\n\n"
                    except json.JSONDecodeError:
                        continue
    except httpx.HTTPError as e:   # 연결 실패·중간 끊김 → 스트림 안에서 안내 후 종료 (주소는 응답에 넣지 않음)
        log.warning("게이트웨이 스트림 오류: %s", type(e).__name__)
        yield f"data: {json.dumps({'token': GATEWAY_DOWN})}\n\n"
        yield "data: [DONE]\n\n"


GATEWAY_MISSING = {"error": "게이트웨이 주소가 설정되지 않았습니다 (NURSE_GATEWAY)"}
GATEWAY_DOWN    = "⚠️ 모델 게이트웨이에 연결할 수 없습니다."


# ── 화면 ─────────────────────────────────────────────────
@app.get("/")
async def index():
    return FileResponse(os.path.join(BASE_DIR, "index.html"), headers={"Cache-Control": "no-cache"})


# ── 질문·임베딩 ──────────────────────────────────────────
@app.post("/api/chat")
async def chat(req: ChatRequest):
    if not GATEWAY:
        return JSONResponse(status_code=503, content=GATEWAY_MISSING)
    return StreamingResponse(
        stream_ollama(req.question, req.context),
        media_type="text/event-stream",
        headers={"Cache-Control": "no-cache", "X-Accel-Buffering": "no"},
    )


@app.post("/api/embed")
async def embed(req: EmbedRequest):
    """텍스트 배열을 bge-m3 벡터로 변환."""
    if not GATEWAY:
        return JSONResponse(status_code=503, content=GATEWAY_MISSING)
    try:
        async with httpx.AsyncClient(timeout=120) as client:
            res = await client.post(
                OLLAMA_EMBED,
                json={"model": EMBED_MODEL, "input": req.texts},
            )
            data = res.json()
    except (httpx.HTTPError, ValueError) as e:   # 연결 실패·JSON 아님 (주소는 응답에 넣지 않음)
        log.warning("게이트웨이 임베딩 오류: %s", type(e).__name__)
        return JSONResponse(status_code=502, content={"error": "모델 게이트웨이에 연결할 수 없습니다."})
    return {"embeddings": data.get("embeddings", []) if isinstance(data, dict) else []}


# ── 자동 로딩 ────────────────────────────────────────────
def _load_data():
    meta = read_meta()
    cats = {c["id"]: c["name"] for c in meta["categories"]}
    files, chunks = [], []
    for fname in doc_files():
        e = doc_entry(meta, fname)
        title = e["title"] or os.path.splitext(fname)[0]
        cs, err = cached_parse(fname, title, e["revised"])
        if cs is None:
            files.append({"filename": fname, "error": err})
            continue
        cat = cats.get(e["category"], "미분류")
        files.append({"filename": fname, "doc": title, "chunks": len(cs)})
        chunks += [dict(c, category=cat) for c in cs]   # 캐시 원본은 건드리지 않는다
    return {"files": files, "chunks": chunks}


@app.get("/api/load-data")
async def load_data():
    """DATA_DIR 폴더를 자동 파싱하여 조항 단위 청크 반환 (_meta.json 의 문서명·개정일·분류를 합침)."""
    if not os.path.isdir(DATA_DIR):
        return JSONResponse(status_code=404, content={"error": "자료 폴더를 찾을 수 없습니다."})
    return await asyncio.to_thread(_load_data)


# ── 분류 관리 ────────────────────────────────────────────
@app.get("/api/categories")
async def get_categories():
    return {"categories": public_categories(read_meta())}


@app.put("/api/categories", dependencies=[Depends(require_admin)])
async def put_categories(req: CategoriesRequest):
    """분류 전체 목록(순서 포함)을 받아 저장. 자료가 들어 있는 분류는 삭제할 수 없다."""
    if len(req.categories) > MAX_CATS:
        fail(400, f"분류는 최대 {MAX_CATS}개까지 만들 수 있습니다.")
    async with META_LOCK:
        meta = load_meta_for_write()
        old = {c["id"]: c["name"] for c in meta["categories"]}
        out, names, ids = [], set(), set()
        for item in req.categories:
            name = " ".join(item.name.split())
            if not 1 <= len(name) <= 30:
                fail(400, "분류 이름을 1~30자로 입력하세요.")
            if name.casefold() in names:
                fail(400, f"'{name}' 분류가 중복되었습니다.")
            names.add(name.casefold())
            if item.id is None:
                cid = new_id("c")
            else:
                if item.id not in old or item.id in ids:
                    fail(400, "잘못된 분류 ID입니다.")
                cid = item.id
            ids.add(cid)
            out.append({"id": cid, "name": name})
        for cid, cname in old.items():
            if cid in ids:
                continue
            n = sum(1 for f in doc_files() if doc_entry(meta, f)["category"] == cid)
            if n:
                return JSONResponse(status_code=409,
                                    content={"error": f"'{cname}' 분류에 자료 {n}개가 있어 삭제할 수 없습니다."})
        meta["categories"] = out
        write_meta(meta)
    return {"categories": public_categories(meta)}


# ── 자료 관리 ────────────────────────────────────────────
@app.get("/api/docs")
async def get_docs():
    meta = read_meta()
    return {"categories": public_categories(meta), "docs": await asyncio.to_thread(list_docs, meta)}


@app.post("/api/docs/upload", dependencies=[Depends(require_admin)])
async def upload_doc(file: UploadFile | None = File(None), category: str = Form(""), title: str = Form(""),
                     revised: str = Form(""), author: str = Form(""), replace_id: str = Form("")):
    """자료 등록(PDF·DOCX). 저장 파일명은 uuid — 사용자가 보낸 파일명은 경로에 쓰지 않는다."""
    orig = re.split(r'[\\/]', (file.filename or "") if file else "")[-1]
    if not orig:
        fail(400, "파일을 선택하세요.")
    stem, ext = os.path.splitext(orig)
    ext = ext[1:].lower()
    if ext not in UPLOAD_EXTS:
        fail(400, "PDF·DOCX 파일만 등록할 수 있습니다.")
    author = clean_author(author)
    title = clean_title(title or stem)
    revised = clean_revised(revised)
    replace_id = (replace_id or "").strip()
    if replace_id:
        check_id(replace_id)

    buf, size = io.BytesIO(), 0
    while chunk := await file.read(1024 * 1024):   # 1MB씩 읽다가 한도를 넘으면 바로 중단
        size += len(chunk)
        if size > MAX_UPLOAD:
            fail(413, "파일 크기는 30MB 이하만 등록할 수 있습니다.")
        buf.write(chunk)
    data = buf.getvalue()
    if not sniff_ok(ext, data):
        fail(400, "파일 내용이 확장자와 맞지 않습니다.")
    try:
        await asyncio.to_thread(_try_parse, ext, data, title, revised)
    except Exception as e:
        log.warning("업로드 파싱 실패: %s", e)
        fail(400, "문서를 읽을 수 없습니다. 파일이 손상되었거나 암호가 걸려 있는지 확인하세요.")

    async with META_LOCK:
        meta = load_meta_for_write()
        category = check_category(meta, category)
        old_fname = None
        if replace_id:
            old_fname = find_doc(meta, replace_id)
            if not old_fname:
                fail(404, "교체할 자료를 찾을 수 없습니다.")
        dup = duplicate_of(meta, title, exclude=old_fname)
        if dup:
            return JSONResponse(status_code=409, content={"error": "같은 문서명이 이미 있습니다.", "existing_id": dup})

        fname = f"{uuid.uuid4().hex}.{ext}"
        tmp = os.path.join(DATA_DIR, f"_upload_{uuid.uuid4().hex}.tmp")   # '_' 로 시작 → 자료로 읽히지 않음
        try:
            with open(tmp, "wb") as f:
                f.write(data)
            os.replace(tmp, os.path.join(DATA_DIR, fname))
        finally:
            if os.path.exists(tmp):
                os.remove(tmp)
        # 등록된 자료는 id 유지, 미등록(sha1 id) 자료는 새 id — 옛 파일 삭제가 실패해 남아도 id 가 겹치지 않게
        tracked = old_fname in meta["docs"] and doc_entry(meta, old_fname)["id"] == replace_id
        doc_id = replace_id if tracked else new_id("d")
        if old_fname:
            meta["docs"].pop(old_fname, None)
        meta["docs"][fname] = {"id": doc_id, "category": category, "title": title, "revised": revised,
                               "author": author, "uploaded": date.today().isoformat()}
        try:
            write_meta(meta)
        except Exception:
            os.remove(os.path.join(DATA_DIR, fname))
            raise
        if old_fname:
            _PARSE_CACHE.pop(old_fname, None)
            try:
                os.remove(os.path.join(DATA_DIR, old_fname))
            except OSError as e:   # 파일이 잠겨 있어도 등록은 성공 — 옛 파일은 미분류로 남는다
                log.warning("교체된 옛 파일 삭제 실패 %s: %s", old_fname, type(e).__name__)
        view = await asyncio.to_thread(doc_view, meta, fname)
    return {"doc": view}


@app.put("/api/docs/{doc_id:anyid}", dependencies=[Depends(require_admin)])
async def edit_doc(doc_id: str, req: DocEditRequest):
    """분류·문서명·개정일·등록자 수정 (미등록 파일은 이때 메타 항목이 생긴다)."""
    check_id(doc_id)
    author, title, revised = clean_author(req.author), clean_title(req.title), clean_revised(req.revised)
    async with META_LOCK:
        meta = load_meta_for_write()
        fname = find_doc(meta, doc_id)
        if not fname:
            fail(404, "자료를 찾을 수 없습니다.")
        category = check_category(meta, req.category)
        dup = duplicate_of(meta, title, exclude=fname)
        if dup:
            return JSONResponse(status_code=409, content={"error": "같은 문서명이 이미 있습니다.", "existing_id": dup})
        e = doc_entry(meta, fname)
        e.update(category=category, title=title, revised=revised, author=author)
        meta["docs"][fname] = e
        write_meta(meta)
        view = await asyncio.to_thread(doc_view, meta, fname)
    return {"doc": view}


@app.delete("/api/docs/{doc_id:anyid}", dependencies=[Depends(require_admin)])
async def delete_doc(doc_id: str):
    check_id(doc_id)
    async with META_LOCK:
        meta = load_meta_for_write()
        fname = find_doc(meta, doc_id)
        if not fname:
            fail(404, "자료를 찾을 수 없습니다.")
        os.remove(os.path.join(DATA_DIR, fname))
        _PARSE_CACHE.pop(fname, None)
        if meta["docs"].pop(fname, None) is not None:
            write_meta(meta)
    return {"ok": True}


@app.get("/health")
async def health():
    return {"status": "ok"}


if __name__ == "__main__":
    import uvicorn
    load_env_file()
    GATEWAY, OLLAMA_URL, OLLAMA_EMBED = gateway_urls()   # .env 값 반영
    uvicorn.run(app, host=os.environ.get("NURSE_HOST", "0.0.0.0"), port=int(os.environ.get("NURSE_PORT", PORT)))
