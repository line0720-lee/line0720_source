# 업무 공유 매니저 (Task Share Manager) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 병원 폐쇄망에서 동작하는 업무 공유 매니저 웹앱(카테고리·업무 관리, 대상자 지정 공유, 로그인, 달력, 현황 요약)을 처음부터 끝까지 구현한다.

**Architecture:** Flask 앱 팩토리 + Blueprint(`auth`, `categories`, `tasks`) + SQLite 단일 파일 DB. 백엔드는 pytest + Flask test client로 TDD. 프론트엔드는 Jinja2 템플릿 + Bootstrap(로컬 번들) + 순수 JS(fetch API)로 백엔드 REST API와 통신하고, FullCalendar(로컬 번들)로 달력을 렌더링한다.

**Tech Stack:** Python 3 / Flask / SQLite3(표준 라이브러리) / pytest / Bootstrap 5(로컬 vendored) / FullCalendar 6(로컬 vendored)

## Global Constraints

- 외부 CDN 사용 금지 — 모든 JS/CSS 라이브러리는 `static/vendor/`에 실제 파일로 저장 (spec §1 Where)
- 업무 상태값은 정확히 `'진행중'`, `'완료'`, `'취소'` 세 가지 문자열만 허용 (spec §3)
- 서버 실행 포트는 5050 고정 (spec §2, 저장소 내 기존 5000번 앱과 충돌 방지)
- 비밀번호는 반드시 `werkzeug.security.generate_password_hash`로 해시 저장, 평문 저장 금지 (spec §3)
- 모든 API 에러 응답은 `{"error": "메시지"}` 형태 + 표준 HTTP 상태코드(400/401/403/404/409) (spec §4 2단계)
- 프로젝트 루트: `task-share-manager/` (저장소 루트 바로 아래, 신규 독립 폴더)

---

## Task 1: 프로젝트 스캐폴드 & 설정

**Files:**
- Create: `task-share-manager/requirements.txt`
- Create: `task-share-manager/config.py`
- Create: `task-share-manager/.gitignore`

**Interfaces:**
- Produces: `config.SECRET_KEY` (str), `config.DATABASE` (절대경로 str), `config.BASE_DIR` (절대경로 str)

- [ ] **Step 1: 폴더 및 requirements.txt 생성**

```bash
mkdir -p "C:/Claude(바이브코딩)/Study/task-share-manager"
```

`task-share-manager/requirements.txt`:
```
flask
pytest
```

- [ ] **Step 2: config.py 작성**

`task-share-manager/config.py`:
```python
import os

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
SECRET_KEY = "dev-secret-change-in-production"
DATABASE = os.path.join(BASE_DIR, "tasks.db")
```

- [ ] **Step 3: .gitignore 작성**

`task-share-manager/.gitignore`:
```
venv/
__pycache__/
*.pyc
tasks.db
*.db-journal
.pytest_cache/
```

- [ ] **Step 4: 가상환경 생성 및 패키지 설치**

```bash
cd "C:/Claude(바이브코딩)/Study/task-share-manager"
python -m venv venv
venv/Scripts/pip install -r requirements.txt
```

Expected: 설치 성공, `flask`와 `pytest`가 `venv/Scripts/pip list`에 나타남

- [ ] **Step 5: Commit**

```bash
git add task-share-manager/requirements.txt task-share-manager/config.py task-share-manager/.gitignore
git commit -m "chore: task-share-manager 프로젝트 스캐폴드 생성"
```

---

## Task 2: DB 스키마 & 초기화·시드 스크립트

**Files:**
- Create: `task-share-manager/db/schema.sql`
- Create: `task-share-manager/db/init_db.py`
- Create: `task-share-manager/db/seed.py`
- Test: `task-share-manager/tests/test_db_init.py`

**Interfaces:**
- Consumes: `config.DATABASE` (Task 1)
- Produces: `db.init_db.init_db(db_path)` 함수, 스키마 실행 후 5개 테이블 존재

- [ ] **Step 1: 실패하는 테스트 작성**

`task-share-manager/tests/test_db_init.py`:
```python
import os
import sqlite3
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from db.init_db import init_db


def test_init_db_creates_all_tables(tmp_path):
    db_path = str(tmp_path / "test.db")
    init_db(db_path)

    conn = sqlite3.connect(db_path)
    tables = {
        row[0]
        for row in conn.execute(
            "SELECT name FROM sqlite_master WHERE type='table'"
        ).fetchall()
    }
    conn.close()

    expected = {"users", "categories", "category_shares", "tasks", "task_shares"}
    assert expected.issubset(tables)
```

- [ ] **Step 2: 테스트 실행하여 실패 확인**

```bash
cd "C:/Claude(바이브코딩)/Study/task-share-manager"
venv/Scripts/pytest tests/test_db_init.py -v
```

Expected: FAIL with `ModuleNotFoundError: No module named 'db'`

- [ ] **Step 3: schema.sql 작성**

`task-share-manager/db/schema.sql`:
```sql
CREATE TABLE IF NOT EXISTS users (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  emp_no TEXT UNIQUE NOT NULL,
  name TEXT NOT NULL,
  dept TEXT,
  password_hash TEXT NOT NULL,
  created_at TEXT DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS categories (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT NOT NULL,
  description TEXT,
  owner_id INTEGER NOT NULL REFERENCES users(id)
);

CREATE TABLE IF NOT EXISTS category_shares (
  category_id INTEGER NOT NULL REFERENCES categories(id),
  user_id INTEGER NOT NULL REFERENCES users(id),
  PRIMARY KEY (category_id, user_id)
);

CREATE TABLE IF NOT EXISTS tasks (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  title TEXT NOT NULL,
  content TEXT,
  category_id INTEGER REFERENCES categories(id),
  assignee_id INTEGER NOT NULL REFERENCES users(id),
  start_date TEXT,
  end_date TEXT,
  status TEXT NOT NULL DEFAULT '진행중' CHECK(status IN ('진행중', '완료', '취소')),
  created_at TEXT DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS task_shares (
  task_id INTEGER NOT NULL REFERENCES tasks(id),
  user_id INTEGER NOT NULL REFERENCES users(id),
  PRIMARY KEY (task_id, user_id)
);
```

- [ ] **Step 4: init_db.py 작성**

`task-share-manager/db/init_db.py`:
```python
import os
import sqlite3

_THIS_DIR = os.path.dirname(os.path.abspath(__file__))
SCHEMA_PATH = os.path.join(_THIS_DIR, "schema.sql")
DEFAULT_DB_PATH = os.path.join(os.path.dirname(_THIS_DIR), "tasks.db")


def init_db(db_path=DEFAULT_DB_PATH):
    conn = sqlite3.connect(db_path)
    with open(SCHEMA_PATH, encoding="utf-8") as f:
        conn.executescript(f.read())
    conn.commit()
    conn.close()


if __name__ == "__main__":
    init_db()
    print(f"DB initialized at {DEFAULT_DB_PATH}")
```

- [ ] **Step 5: 테스트 실행하여 통과 확인**

```bash
venv/Scripts/pytest tests/test_db_init.py -v
```

Expected: PASS

- [ ] **Step 6: seed.py 작성 (더미 사용자 3명 / 카테고리 2개 / 업무 5개)**

`task-share-manager/db/seed.py`:
```python
import os
import sqlite3
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from werkzeug.security import generate_password_hash
from db.init_db import DEFAULT_DB_PATH, init_db


def seed(db_path=DEFAULT_DB_PATH):
    init_db(db_path)
    conn = sqlite3.connect(db_path)
    conn.row_factory = sqlite3.Row

    pw = generate_password_hash("1234")
    users = [
        ("E001", "김민준", "정보전략파트", pw),
        ("E002", "이서연", "간호부", pw),
        ("E003", "박도윤", "진료지원팀", pw),
    ]
    for emp_no, name, dept, password_hash in users:
        conn.execute(
            "INSERT OR IGNORE INTO users (emp_no, name, dept, password_hash) VALUES (?, ?, ?, ?)",
            (emp_no, name, dept, password_hash),
        )
    conn.commit()

    owner = conn.execute("SELECT id FROM users WHERE emp_no = 'E001'").fetchone()
    categories = [
        ("일반 업무", "부서 공통 업무", owner["id"]),
        ("수술", "수술 일정 및 준비 업무", owner["id"]),
    ]
    for name, desc, owner_id in categories:
        conn.execute(
            "INSERT INTO categories (name, description, owner_id) VALUES (?, ?, ?)",
            (name, desc, owner_id),
        )
    conn.commit()

    general_cat = conn.execute("SELECT id FROM categories WHERE name = '일반 업무'").fetchone()
    surgery_cat = conn.execute("SELECT id FROM categories WHERE name = '수술'").fetchone()
    e001 = conn.execute("SELECT id FROM users WHERE emp_no = 'E001'").fetchone()
    e002 = conn.execute("SELECT id FROM users WHERE emp_no = 'E002'").fetchone()
    e003 = conn.execute("SELECT id FROM users WHERE emp_no = 'E003'").fetchone()

    tasks = [
        ("전산 점검", "월간 서버 점검", general_cat["id"], e001["id"], "2026-09-01", "2026-09-01", "진행중"),
        ("백업 확인", "DB 백업 로그 확인", general_cat["id"], e001["id"], "2026-09-02", "2026-09-02", "진행중"),
        ("수술 준비", "3번 수술실 장비 점검", surgery_cat["id"], e002["id"], "2026-09-03", "2026-09-03", "진행중"),
        ("환자 안내", "외래 환자 안내 자료 갱신", general_cat["id"], e003["id"], "2026-08-20", "2026-08-25", "완료"),
        ("행사 취소 안내", "취소된 행사 공지", general_cat["id"], e003["id"], "2026-08-10", "2026-08-11", "취소"),
    ]
    for title, content, category_id, assignee_id, start, end, status in tasks:
        conn.execute(
            """INSERT INTO tasks (title, content, category_id, assignee_id, start_date, end_date, status)
               VALUES (?, ?, ?, ?, ?, ?, ?)""",
            (title, content, category_id, assignee_id, start, end, status),
        )
    conn.commit()
    conn.close()


if __name__ == "__main__":
    seed()
    print("Seed data inserted.")
```

- [ ] **Step 7: 시드 스크립트 수동 실행 확인**

```bash
rm -f tasks.db
venv/Scripts/python db/seed.py
venv/Scripts/python -c "import sqlite3; c=sqlite3.connect('tasks.db'); print(c.execute('SELECT count(*) FROM users').fetchone()); print(c.execute('SELECT count(*) FROM tasks').fetchone())"
```

Expected: `(3,)` 와 `(5,)` 출력

- [ ] **Step 8: Commit**

```bash
git add task-share-manager/db task-share-manager/tests/test_db_init.py
git commit -m "feat: DB 스키마 및 초기화/시드 스크립트 추가"
```

---

## Task 3: Flask 앱 팩토리 + DB 커넥션 헬퍼 + /health

**Files:**
- Create: `task-share-manager/db_conn.py`
- Create: `task-share-manager/app.py`
- Create: `task-share-manager/tests/conftest.py`
- Test: `task-share-manager/tests/test_health.py`

**Interfaces:**
- Produces: `db_conn.get_db()` (sqlite3.Connection, row_factory=Row), `db_conn.init_app(app)`, `app.create_app(test_config=None)` (Flask 인스턴스)
- Consumes: `config.py` (Task 1), `db/schema.sql` (Task 2)

- [ ] **Step 1: conftest.py 작성 (테스트용 임시 DB + Flask 클라이언트 픽스처)**

`task-share-manager/tests/conftest.py`:
```python
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import pytest

from app import create_app
from db.init_db import init_db


@pytest.fixture
def app(tmp_path):
    db_path = str(tmp_path / "test.db")
    init_db(db_path)
    application = create_app({"DATABASE": db_path, "TESTING": True, "SECRET_KEY": "test"})
    yield application


@pytest.fixture
def client(app):
    return app.test_client()
```

- [ ] **Step 2: 실패하는 테스트 작성**

`task-share-manager/tests/test_health.py`:
```python
def test_health_returns_ok(client):
    resp = client.get("/health")
    assert resp.status_code == 200
    data = resp.get_json()
    assert data == {"status": "ok", "db": "connected"}
```

- [ ] **Step 3: 테스트 실행하여 실패 확인**

```bash
venv/Scripts/pytest tests/test_health.py -v
```

Expected: FAIL with `ModuleNotFoundError: No module named 'app'`

- [ ] **Step 4: db_conn.py 작성**

`task-share-manager/db_conn.py`:
```python
import sqlite3

from flask import current_app, g


def get_db():
    if "db" not in g:
        g.db = sqlite3.connect(current_app.config["DATABASE"])
        g.db.row_factory = sqlite3.Row
        g.db.execute("PRAGMA foreign_keys = ON")
    return g.db


def close_db(e=None):
    db = g.pop("db", None)
    if db is not None:
        db.close()


def init_app(app):
    app.teardown_appcontext(close_db)
```

- [ ] **Step 5: app.py 작성 (앱 팩토리 + /health)**

`task-share-manager/app.py`:
```python
from flask import Flask, jsonify

import config
import db_conn
from auth import auth_bp
from categories import categories_bp
from tasks import tasks_bp


def create_app(test_config=None):
    app = Flask(__name__)
    app.config.from_object(config)
    if test_config:
        app.config.update(test_config)

    db_conn.init_app(app)
    app.register_blueprint(auth_bp)
    app.register_blueprint(categories_bp)
    app.register_blueprint(tasks_bp)

    @app.route("/health")
    def health():
        try:
            db_conn.get_db().execute("SELECT 1")
            return jsonify(status="ok", db="connected")
        except Exception:
            return jsonify(status="error", db="disconnected"), 500

    return app


app = create_app()

if __name__ == "__main__":
    app.run(port=5050, debug=True)
```

> Task 3 시점에는 `auth.py`, `categories.py`, `tasks.py`가 아직 없으므로 Step 6에서 최소 스텁을 먼저 만든다 (실제 라우트는 Task 4~6에서 채운다).

- [ ] **Step 6: 최소 blueprint 스텁 생성**

`task-share-manager/auth.py`:
```python
from flask import Blueprint

auth_bp = Blueprint("auth", __name__, url_prefix="/api")
```

`task-share-manager/categories.py`:
```python
from flask import Blueprint

categories_bp = Blueprint("categories", __name__, url_prefix="/api/categories")
```

`task-share-manager/tasks.py`:
```python
from flask import Blueprint

tasks_bp = Blueprint("tasks", __name__, url_prefix="/api/tasks")
```

- [ ] **Step 7: 테스트 실행하여 통과 확인**

```bash
venv/Scripts/pytest tests/test_health.py -v
```

Expected: PASS

- [ ] **Step 8: Commit**

```bash
git add task-share-manager/db_conn.py task-share-manager/app.py task-share-manager/auth.py \
        task-share-manager/categories.py task-share-manager/tasks.py task-share-manager/tests/conftest.py \
        task-share-manager/tests/test_health.py
git commit -m "feat: Flask 앱 팩토리, DB 커넥션 헬퍼, /health 라우트 추가"
```

---

## Task 4: 인증 API (회원가입/로그인/로그아웃/me)

**Files:**
- Modify: `task-share-manager/auth.py`
- Test: `task-share-manager/tests/test_auth.py`

**Interfaces:**
- Consumes: `db_conn.get_db()` (Task 3)
- Produces: `auth.login_required(f)` 데코레이터 — 이후 Task 5, 6에서 사용. 세션 키 `session['user_id']` (int)

- [ ] **Step 1: 실패하는 테스트 작성**

`task-share-manager/tests/test_auth.py`:
```python
def register(client, emp_no="E100", name="테스트", dept="IT", password="pw1234"):
    return client.post(
        "/api/register",
        json={"emp_no": emp_no, "name": name, "dept": dept, "password": password},
    )


def test_register_then_login_succeeds(client):
    resp = register(client)
    assert resp.status_code == 201

    resp = client.post("/api/login", json={"emp_no": "E100", "password": "pw1234"})
    assert resp.status_code == 200
    assert resp.get_json()["emp_no"] == "E100"


def test_login_with_wrong_password_fails(client):
    register(client)
    resp = client.post("/api/login", json={"emp_no": "E100", "password": "wrong"})
    assert resp.status_code == 401
    assert "error" in resp.get_json()


def test_duplicate_emp_no_register_fails(client):
    register(client)
    resp = register(client)
    assert resp.status_code == 409


def test_me_requires_login(client):
    resp = client.get("/api/me")
    assert resp.status_code == 401


def test_me_returns_current_user_after_login(client):
    register(client)
    client.post("/api/login", json={"emp_no": "E100", "password": "pw1234"})
    resp = client.get("/api/me")
    assert resp.status_code == 200
    assert resp.get_json()["emp_no"] == "E100"


def test_logout_clears_session(client):
    register(client)
    client.post("/api/login", json={"emp_no": "E100", "password": "pw1234"})
    client.post("/api/logout")
    resp = client.get("/api/me")
    assert resp.status_code == 401
```

- [ ] **Step 2: 테스트 실행하여 실패 확인**

```bash
venv/Scripts/pytest tests/test_auth.py -v
```

Expected: FAIL (404, 라우트 없음)

- [ ] **Step 3: auth.py 구현**

`task-share-manager/auth.py`:
```python
from functools import wraps

from flask import Blueprint, jsonify, request, session
from werkzeug.security import check_password_hash, generate_password_hash

from db_conn import get_db

auth_bp = Blueprint("auth", __name__, url_prefix="/api")


def login_required(f):
    @wraps(f)
    def wrapper(*args, **kwargs):
        if "user_id" not in session:
            return jsonify(error="로그인이 필요합니다"), 401
        return f(*args, **kwargs)

    return wrapper


# curl -X POST http://localhost:5050/api/register \
#   -H "Content-Type: application/json" \
#   -d '{"emp_no":"E100","name":"테스트","dept":"IT","password":"pw1234"}'
@auth_bp.route("/register", methods=["POST"])
def register():
    data = request.get_json(silent=True) or {}
    emp_no = data.get("emp_no")
    name = data.get("name")
    password = data.get("password")
    if not emp_no or not name or not password:
        return jsonify(error="emp_no, name, password는 필수입니다"), 400

    db = get_db()
    if db.execute("SELECT id FROM users WHERE emp_no = ?", (emp_no,)).fetchone():
        return jsonify(error="이미 등록된 사번입니다"), 409

    db.execute(
        "INSERT INTO users (emp_no, name, dept, password_hash) VALUES (?, ?, ?, ?)",
        (emp_no, name, data.get("dept"), generate_password_hash(password)),
    )
    db.commit()
    return jsonify(message="등록되었습니다"), 201


# curl -X POST http://localhost:5050/api/login \
#   -H "Content-Type: application/json" -d '{"emp_no":"E100","password":"pw1234"}' -c cookies.txt
@auth_bp.route("/login", methods=["POST"])
def login():
    data = request.get_json(silent=True) or {}
    emp_no = data.get("emp_no")
    password = data.get("password")
    if not emp_no or not password:
        return jsonify(error="emp_no, password는 필수입니다"), 400

    db = get_db()
    user = db.execute("SELECT * FROM users WHERE emp_no = ?", (emp_no,)).fetchone()
    if user is None or not check_password_hash(user["password_hash"], password):
        return jsonify(error="사번 또는 비밀번호가 올바르지 않습니다"), 401

    session["user_id"] = user["id"]
    return jsonify(id=user["id"], emp_no=user["emp_no"], name=user["name"], dept=user["dept"])


# curl -X POST http://localhost:5050/api/logout -b cookies.txt
@auth_bp.route("/logout", methods=["POST"])
def logout():
    session.pop("user_id", None)
    return jsonify(message="로그아웃되었습니다")


# curl http://localhost:5050/api/me -b cookies.txt
@auth_bp.route("/me", methods=["GET"])
@login_required
def me():
    db = get_db()
    user = db.execute(
        "SELECT id, emp_no, name, dept FROM users WHERE id = ?", (session["user_id"],)
    ).fetchone()
    return jsonify(dict(user))
```

- [ ] **Step 4: 테스트 실행하여 통과 확인**

```bash
venv/Scripts/pytest tests/test_auth.py -v
```

Expected: PASS (6개 테스트 모두 통과)

- [ ] **Step 5: Commit**

```bash
git add task-share-manager/auth.py task-share-manager/tests/test_auth.py
git commit -m "feat: 회원가입/로그인/로그아웃/me API 및 login_required 데코레이터 추가"
```

---

## Task 5: 권한 헬퍼 + 카테고리 API

**Files:**
- Create: `task-share-manager/permissions.py`
- Modify: `task-share-manager/categories.py`
- Test: `task-share-manager/tests/test_categories.py`

**Interfaces:**
- Consumes: `auth.login_required` (Task 4), `db_conn.get_db()` (Task 3)
- Produces: `permissions.can_view_category(db, category_id, user_id) -> bool` — Task 6에서 재사용

- [ ] **Step 1: 실패하는 테스트 작성**

`task-share-manager/tests/test_categories.py`:
```python
def _register_and_login(client, emp_no, name="사용자", password="pw1234"):
    client.post("/api/register", json={"emp_no": emp_no, "name": name, "dept": "IT", "password": password})
    client.post("/api/login", json={"emp_no": emp_no, "password": password})


def test_create_and_list_category(client):
    _register_and_login(client, "E200")
    resp = client.post("/api/categories", json={"name": "테스트카테고리", "description": "설명"})
    assert resp.status_code == 201

    resp = client.get("/api/categories")
    names = [c["name"] for c in resp.get_json()]
    assert "테스트카테고리" in names


def test_non_owner_cannot_view_unshared_category(client):
    _register_and_login(client, "E201")
    created = client.post("/api/categories", json={"name": "비공개", "description": ""}).get_json()

    client.post("/api/logout")
    _register_and_login(client, "E202")
    resp = client.get(f"/api/categories/{created['id']}")
    assert resp.status_code == 403


def test_shared_user_can_view_category(client):
    _register_and_login(client, "E203")
    created = client.post("/api/categories", json={"name": "공유대상", "description": ""}).get_json()

    client.post("/api/logout")
    _register_and_login(client, "E204")
    me = client.get("/api/me").get_json()

    client.post("/api/logout")
    _register_and_login(client, "E203", password="pw1234")
    share_resp = client.post(f"/api/categories/{created['id']}/shares", json={"user_id": me["id"]})
    assert share_resp.status_code == 201

    client.post("/api/logout")
    _register_and_login(client, "E204", password="pw1234")
    resp = client.get(f"/api/categories/{created['id']}")
    assert resp.status_code == 200


def test_only_owner_can_delete_category(client):
    _register_and_login(client, "E205")
    created = client.post("/api/categories", json={"name": "삭제대상", "description": ""}).get_json()

    client.post("/api/logout")
    _register_and_login(client, "E206")
    resp = client.delete(f"/api/categories/{created['id']}")
    assert resp.status_code == 403
```

- [ ] **Step 2: 테스트 실행하여 실패 확인**

```bash
venv/Scripts/pytest tests/test_categories.py -v
```

Expected: FAIL (404, 라우트 없음)

- [ ] **Step 3: permissions.py 작성**

`task-share-manager/permissions.py`:
```python
def can_view_category(db, category_id, user_id):
    row = db.execute("SELECT owner_id FROM categories WHERE id = ?", (category_id,)).fetchone()
    if row is None:
        return False
    if row["owner_id"] == user_id:
        return True
    shared = db.execute(
        "SELECT 1 FROM category_shares WHERE category_id = ? AND user_id = ?",
        (category_id, user_id),
    ).fetchone()
    return shared is not None


def can_view_task(db, task, user_id):
    if task["assignee_id"] == user_id:
        return True
    if task["category_id"] is not None and can_view_category(db, task["category_id"], user_id):
        return True
    shared = db.execute(
        "SELECT 1 FROM task_shares WHERE task_id = ? AND user_id = ?",
        (task["id"], user_id),
    ).fetchone()
    return shared is not None
```

- [ ] **Step 4: categories.py 구현**

`task-share-manager/categories.py`:
```python
from flask import Blueprint, jsonify, request, session

from auth import login_required
from db_conn import get_db
from permissions import can_view_category

categories_bp = Blueprint("categories", __name__, url_prefix="/api/categories")


# curl -X POST http://localhost:5050/api/categories -b cookies.txt \
#   -H "Content-Type: application/json" -d '{"name":"수술","description":"수술 일정"}'
@categories_bp.route("", methods=["POST"])
@login_required
def create_category():
    data = request.get_json(silent=True) or {}
    name = data.get("name")
    if not name:
        return jsonify(error="name은 필수입니다"), 400

    db = get_db()
    cur = db.execute(
        "INSERT INTO categories (name, description, owner_id) VALUES (?, ?, ?)",
        (name, data.get("description"), session["user_id"]),
    )
    db.commit()
    return (
        jsonify(id=cur.lastrowid, name=name, description=data.get("description"), owner_id=session["user_id"]),
        201,
    )


# curl http://localhost:5050/api/categories -b cookies.txt
@categories_bp.route("", methods=["GET"])
@login_required
def list_categories():
    db = get_db()
    user_id = session["user_id"]
    rows = db.execute(
        """SELECT DISTINCT c.* FROM categories c
           LEFT JOIN category_shares cs ON cs.category_id = c.id
           WHERE c.owner_id = ? OR cs.user_id = ?""",
        (user_id, user_id),
    ).fetchall()
    return jsonify([dict(r) for r in rows])


# curl http://localhost:5050/api/categories/1 -b cookies.txt
@categories_bp.route("/<int:category_id>", methods=["GET"])
@login_required
def get_category(category_id):
    db = get_db()
    if not can_view_category(db, category_id, session["user_id"]):
        return jsonify(error="조회 권한이 없습니다"), 403
    row = db.execute("SELECT * FROM categories WHERE id = ?", (category_id,)).fetchone()
    if row is None:
        return jsonify(error="카테고리를 찾을 수 없습니다"), 404
    return jsonify(dict(row))


# curl -X PUT http://localhost:5050/api/categories/1 -b cookies.txt \
#   -H "Content-Type: application/json" -d '{"name":"수정된 이름"}'
@categories_bp.route("/<int:category_id>", methods=["PUT"])
@login_required
def update_category(category_id):
    db = get_db()
    row = db.execute("SELECT * FROM categories WHERE id = ?", (category_id,)).fetchone()
    if row is None:
        return jsonify(error="카테고리를 찾을 수 없습니다"), 404
    if row["owner_id"] != session["user_id"]:
        return jsonify(error="소유자만 수정할 수 있습니다"), 403

    data = request.get_json(silent=True) or {}
    name = data.get("name", row["name"])
    description = data.get("description", row["description"])
    db.execute("UPDATE categories SET name = ?, description = ? WHERE id = ?", (name, description, category_id))
    db.commit()
    return jsonify(id=category_id, name=name, description=description, owner_id=row["owner_id"])


# curl -X DELETE http://localhost:5050/api/categories/1 -b cookies.txt
@categories_bp.route("/<int:category_id>", methods=["DELETE"])
@login_required
def delete_category(category_id):
    db = get_db()
    row = db.execute("SELECT * FROM categories WHERE id = ?", (category_id,)).fetchone()
    if row is None:
        return jsonify(error="카테고리를 찾을 수 없습니다"), 404
    if row["owner_id"] != session["user_id"]:
        return jsonify(error="소유자만 삭제할 수 있습니다"), 403

    db.execute("DELETE FROM categories WHERE id = ?", (category_id,))
    db.commit()
    return jsonify(message="삭제되었습니다")


# curl -X POST http://localhost:5050/api/categories/1/shares -b cookies.txt \
#   -H "Content-Type: application/json" -d '{"user_id":2}'
@categories_bp.route("/<int:category_id>/shares", methods=["POST"])
@login_required
def add_category_share(category_id):
    db = get_db()
    row = db.execute("SELECT * FROM categories WHERE id = ?", (category_id,)).fetchone()
    if row is None:
        return jsonify(error="카테고리를 찾을 수 없습니다"), 404
    if row["owner_id"] != session["user_id"]:
        return jsonify(error="소유자만 공유를 등록할 수 있습니다"), 403

    data = request.get_json(silent=True) or {}
    user_id = data.get("user_id")
    if not user_id or db.execute("SELECT id FROM users WHERE id = ?", (user_id,)).fetchone() is None:
        return jsonify(error="존재하지 않는 사용자입니다"), 400

    db.execute("INSERT OR IGNORE INTO category_shares (category_id, user_id) VALUES (?, ?)", (category_id, user_id))
    db.commit()
    return jsonify(message="공유 대상자가 등록되었습니다"), 201


# curl -X DELETE http://localhost:5050/api/categories/1/shares/2 -b cookies.txt
@categories_bp.route("/<int:category_id>/shares/<int:user_id>", methods=["DELETE"])
@login_required
def remove_category_share(category_id, user_id):
    db = get_db()
    row = db.execute("SELECT * FROM categories WHERE id = ?", (category_id,)).fetchone()
    if row is None:
        return jsonify(error="카테고리를 찾을 수 없습니다"), 404
    if row["owner_id"] != session["user_id"]:
        return jsonify(error="소유자만 공유를 해제할 수 있습니다"), 403

    db.execute("DELETE FROM category_shares WHERE category_id = ? AND user_id = ?", (category_id, user_id))
    db.commit()
    return jsonify(message="공유가 해제되었습니다")
```

- [ ] **Step 5: 테스트 실행하여 통과 확인**

```bash
venv/Scripts/pytest tests/test_categories.py -v
```

Expected: PASS (4개 테스트 모두 통과)

- [ ] **Step 6: Commit**

```bash
git add task-share-manager/permissions.py task-share-manager/categories.py task-share-manager/tests/test_categories.py
git commit -m "feat: 카테고리 CRUD 및 공유 API, 권한 헬퍼 추가"
```

---

## Task 6: 업무 API (CRUD + 상태 변경 + 공유 + 유효성 검사)

**Files:**
- Modify: `task-share-manager/tasks.py`
- Test: `task-share-manager/tests/test_tasks.py`

**Interfaces:**
- Consumes: `auth.login_required` (Task 4), `permissions.can_view_task` (Task 5), `db_conn.get_db()` (Task 3)
- Produces: `/api/tasks*` 엔드포인트 — Task 9~12(프론트엔드)에서 fetch로 호출

- [ ] **Step 1: 실패하는 테스트 작성 (CRUD/권한/상태변경/공유/유효성 검사 포함)**

`task-share-manager/tests/test_tasks.py`:
```python
def _register_and_login(client, emp_no, password="pw1234"):
    client.post("/api/register", json={"emp_no": emp_no, "name": "사용자", "dept": "IT", "password": password})
    client.post("/api/login", json={"emp_no": emp_no, "password": password})
    return client.get("/api/me").get_json()


def test_create_and_get_own_task(client):
    me = _register_and_login(client, "T001")
    resp = client.post(
        "/api/tasks",
        json={"title": "제목", "assignee_id": me["id"], "start_date": "2026-09-01", "end_date": "2026-09-02"},
    )
    assert resp.status_code == 201
    task_id = resp.get_json()["id"]

    resp = client.get(f"/api/tasks/{task_id}")
    assert resp.status_code == 200
    assert resp.get_json()["status"] == "진행중"


def test_create_task_missing_title_fails(client):
    me = _register_and_login(client, "T002")
    resp = client.post("/api/tasks", json={"assignee_id": me["id"]})
    assert resp.status_code == 400


def test_create_task_end_before_start_fails(client):
    me = _register_and_login(client, "T003")
    resp = client.post(
        "/api/tasks",
        json={"title": "제목", "assignee_id": me["id"], "start_date": "2026-09-10", "end_date": "2026-09-01"},
    )
    assert resp.status_code == 400


def test_other_user_cannot_view_unshared_task(client):
    me = _register_and_login(client, "T004")
    created = client.post("/api/tasks", json={"title": "비공개 업무", "assignee_id": me["id"]}).get_json()

    client.post("/api/logout")
    _register_and_login(client, "T005")
    resp = client.get(f"/api/tasks/{created['id']}")
    assert resp.status_code == 403


def test_task_share_grants_visibility(client):
    me = _register_and_login(client, "T006")
    created = client.post("/api/tasks", json={"title": "공유 업무", "assignee_id": me["id"]}).get_json()

    client.post("/api/logout")
    other = _register_and_login(client, "T007")

    client.post("/api/logout")
    _register_and_login(client, "T006")
    share_resp = client.post(f"/api/tasks/{created['id']}/shares", json={"user_id": other["id"]})
    assert share_resp.status_code == 201

    client.post("/api/logout")
    _register_and_login(client, "T007")
    resp = client.get(f"/api/tasks/{created['id']}")
    assert resp.status_code == 200


def test_update_status_to_completed(client):
    me = _register_and_login(client, "T008")
    created = client.post("/api/tasks", json={"title": "완료할 업무", "assignee_id": me["id"]}).get_json()

    resp = client.patch(f"/api/tasks/{created['id']}/status", json={"status": "완료"})
    assert resp.status_code == 200
    assert resp.get_json()["status"] == "완료"


def test_update_status_with_invalid_value_fails(client):
    me = _register_and_login(client, "T009")
    created = client.post("/api/tasks", json={"title": "업무", "assignee_id": me["id"]}).get_json()

    resp = client.patch(f"/api/tasks/{created['id']}/status", json={"status": "보류"})
    assert resp.status_code == 400


def test_list_tasks_only_shows_visible_tasks(client):
    me = _register_and_login(client, "T010")
    client.post("/api/tasks", json={"title": "내 업무", "assignee_id": me["id"]})

    client.post("/api/logout")
    _register_and_login(client, "T011")
    resp = client.get("/api/tasks")
    titles = [t["title"] for t in resp.get_json()]
    assert "내 업무" not in titles
```

- [ ] **Step 2: 테스트 실행하여 실패 확인**

```bash
venv/Scripts/pytest tests/test_tasks.py -v
```

Expected: FAIL (404, 라우트 없음)

- [ ] **Step 3: tasks.py 구현**

`task-share-manager/tasks.py`:
```python
from flask import Blueprint, jsonify, request, session

from auth import login_required
from db_conn import get_db
from permissions import can_view_task

tasks_bp = Blueprint("tasks", __name__, url_prefix="/api/tasks")

VALID_STATUSES = ("진행중", "완료", "취소")


def _validate_task_payload(data, db):
    if not data.get("title"):
        return "제목은 필수입니다"
    if not data.get("assignee_id"):
        return "담당자는 필수입니다"
    if db.execute("SELECT id FROM users WHERE id = ?", (data["assignee_id"],)).fetchone() is None:
        return "존재하지 않는 담당자입니다"

    category_id = data.get("category_id")
    if category_id is not None:
        if db.execute("SELECT id FROM categories WHERE id = ?", (category_id,)).fetchone() is None:
            return "존재하지 않는 카테고리입니다"

    start_date = data.get("start_date")
    end_date = data.get("end_date")
    if start_date and end_date and end_date < start_date:
        return "종료일은 시작일보다 빠를 수 없습니다"

    status = data.get("status", "진행중")
    if status not in VALID_STATUSES:
        return "status는 진행중/완료/취소 중 하나여야 합니다"

    return None


# curl -X POST http://localhost:5050/api/tasks -b cookies.txt -H "Content-Type: application/json" \
#   -d '{"title":"전산 점검","assignee_id":1,"category_id":1,"start_date":"2026-09-01","end_date":"2026-09-02"}'
@tasks_bp.route("", methods=["POST"])
@login_required
def create_task():
    data = request.get_json(silent=True) or {}
    db = get_db()
    error = _validate_task_payload(data, db)
    if error:
        return jsonify(error=error), 400

    cur = db.execute(
        """INSERT INTO tasks (title, content, category_id, assignee_id, start_date, end_date, status)
           VALUES (?, ?, ?, ?, ?, ?, ?)""",
        (
            data["title"],
            data.get("content"),
            data.get("category_id"),
            data["assignee_id"],
            data.get("start_date"),
            data.get("end_date"),
            data.get("status", "진행중"),
        ),
    )
    db.commit()
    task = db.execute("SELECT * FROM tasks WHERE id = ?", (cur.lastrowid,)).fetchone()
    return jsonify(dict(task)), 201


# curl "http://localhost:5050/api/tasks?status=진행중&category_id=1" -b cookies.txt
@tasks_bp.route("", methods=["GET"])
@login_required
def list_tasks():
    db = get_db()
    user_id = session["user_id"]
    status_filter = request.args.get("status")
    category_filter = request.args.get("category_id")

    rows = db.execute("SELECT * FROM tasks").fetchall()
    visible = [dict(r) for r in rows if can_view_task(db, r, user_id)]
    if status_filter:
        visible = [t for t in visible if t["status"] == status_filter]
    if category_filter:
        visible = [t for t in visible if str(t["category_id"]) == str(category_filter)]
    return jsonify(visible)


# curl http://localhost:5050/api/tasks/1 -b cookies.txt
@tasks_bp.route("/<int:task_id>", methods=["GET"])
@login_required
def get_task(task_id):
    db = get_db()
    task = db.execute("SELECT * FROM tasks WHERE id = ?", (task_id,)).fetchone()
    if task is None:
        return jsonify(error="업무를 찾을 수 없습니다"), 404
    if not can_view_task(db, task, session["user_id"]):
        return jsonify(error="조회 권한이 없습니다"), 403
    return jsonify(dict(task))


# curl -X PUT http://localhost:5050/api/tasks/1 -b cookies.txt -H "Content-Type: application/json" \
#   -d '{"title":"수정된 제목"}'
@tasks_bp.route("/<int:task_id>", methods=["PUT"])
@login_required
def update_task(task_id):
    db = get_db()
    task = db.execute("SELECT * FROM tasks WHERE id = ?", (task_id,)).fetchone()
    if task is None:
        return jsonify(error="업무를 찾을 수 없습니다"), 404
    if task["assignee_id"] != session["user_id"]:
        return jsonify(error="담당자만 수정할 수 있습니다"), 403

    data = request.get_json(silent=True) or {}
    merged = dict(task)
    for key in ("title", "content", "category_id", "assignee_id", "start_date", "end_date", "status"):
        if key in data:
            merged[key] = data[key]

    error = _validate_task_payload(merged, db)
    if error:
        return jsonify(error=error), 400

    db.execute(
        """UPDATE tasks SET title=?, content=?, category_id=?, assignee_id=?, start_date=?, end_date=?, status=?
           WHERE id=?""",
        (
            merged["title"],
            merged["content"],
            merged["category_id"],
            merged["assignee_id"],
            merged["start_date"],
            merged["end_date"],
            merged["status"],
            task_id,
        ),
    )
    db.commit()
    updated = db.execute("SELECT * FROM tasks WHERE id = ?", (task_id,)).fetchone()
    return jsonify(dict(updated))


# curl -X DELETE http://localhost:5050/api/tasks/1 -b cookies.txt
@tasks_bp.route("/<int:task_id>", methods=["DELETE"])
@login_required
def delete_task(task_id):
    db = get_db()
    task = db.execute("SELECT * FROM tasks WHERE id = ?", (task_id,)).fetchone()
    if task is None:
        return jsonify(error="업무를 찾을 수 없습니다"), 404
    if task["assignee_id"] != session["user_id"]:
        return jsonify(error="담당자만 삭제할 수 있습니다"), 403

    db.execute("DELETE FROM tasks WHERE id = ?", (task_id,))
    db.commit()
    return jsonify(message="삭제되었습니다")


# curl -X PATCH http://localhost:5050/api/tasks/1/status -b cookies.txt -H "Content-Type: application/json" \
#   -d '{"status":"완료"}'
@tasks_bp.route("/<int:task_id>/status", methods=["PATCH"])
@login_required
def update_status(task_id):
    db = get_db()
    task = db.execute("SELECT * FROM tasks WHERE id = ?", (task_id,)).fetchone()
    if task is None:
        return jsonify(error="업무를 찾을 수 없습니다"), 404
    if not can_view_task(db, task, session["user_id"]):
        return jsonify(error="조회 권한이 없습니다"), 403

    data = request.get_json(silent=True) or {}
    status = data.get("status")
    if status not in VALID_STATUSES:
        return jsonify(error="status는 진행중/완료/취소 중 하나여야 합니다"), 400

    db.execute("UPDATE tasks SET status = ? WHERE id = ?", (status, task_id))
    db.commit()
    return jsonify(id=task_id, status=status)


# curl -X POST http://localhost:5050/api/tasks/1/shares -b cookies.txt -H "Content-Type: application/json" \
#   -d '{"user_id":2}'
@tasks_bp.route("/<int:task_id>/shares", methods=["POST"])
@login_required
def add_task_share(task_id):
    db = get_db()
    task = db.execute("SELECT * FROM tasks WHERE id = ?", (task_id,)).fetchone()
    if task is None:
        return jsonify(error="업무를 찾을 수 없습니다"), 404
    if task["assignee_id"] != session["user_id"]:
        return jsonify(error="담당자만 공유를 등록할 수 있습니다"), 403

    data = request.get_json(silent=True) or {}
    user_id = data.get("user_id")
    if not user_id or db.execute("SELECT id FROM users WHERE id = ?", (user_id,)).fetchone() is None:
        return jsonify(error="존재하지 않는 사용자입니다"), 400

    db.execute("INSERT OR IGNORE INTO task_shares (task_id, user_id) VALUES (?, ?)", (task_id, user_id))
    db.commit()
    return jsonify(message="공유 대상자가 등록되었습니다"), 201


# curl -X DELETE http://localhost:5050/api/tasks/1/shares/2 -b cookies.txt
@tasks_bp.route("/<int:task_id>/shares/<int:user_id>", methods=["DELETE"])
@login_required
def remove_task_share(task_id, user_id):
    db = get_db()
    task = db.execute("SELECT * FROM tasks WHERE id = ?", (task_id,)).fetchone()
    if task is None:
        return jsonify(error="업무를 찾을 수 없습니다"), 404
    if task["assignee_id"] != session["user_id"]:
        return jsonify(error="담당자만 공유를 해제할 수 있습니다"), 403

    db.execute("DELETE FROM task_shares WHERE task_id = ? AND user_id = ?", (task_id, user_id))
    db.commit()
    return jsonify(message="공유가 해제되었습니다")
```

- [ ] **Step 4: 테스트 실행하여 통과 확인**

```bash
venv/Scripts/pytest tests/ -v
```

Expected: PASS (Task 2~6에서 작성한 모든 테스트 통과, 총 20개 내외)

- [ ] **Step 5: Commit**

```bash
git add task-share-manager/tasks.py task-share-manager/tests/test_tasks.py
git commit -m "feat: 업무 CRUD, 상태 변경, 공유 API 및 유효성 검사 추가"
```

---

## Task 7: 정적 자산 로컬 번들 (Bootstrap, FullCalendar)

**Files:**
- Create: `task-share-manager/static/vendor/bootstrap/bootstrap.min.css`
- Create: `task-share-manager/static/vendor/bootstrap/bootstrap.bundle.min.js`
- Create: `task-share-manager/static/vendor/fullcalendar/index.global.min.js`

**Interfaces:**
- Produces: `/static/vendor/bootstrap/*`, `/static/vendor/fullcalendar/*` — Task 8, 11에서 `<link>`/`<script>`로 참조

- [ ] **Step 1: 폴더 생성 및 파일 다운로드 (빌드 환경에서 1회 수행 — 폐쇄망 배포 시에는 이렇게 받은 파일을 그대로 복사)**

```bash
mkdir -p "C:/Claude(바이브코딩)/Study/task-share-manager/static/vendor/bootstrap"
mkdir -p "C:/Claude(바이브코딩)/Study/task-share-manager/static/vendor/fullcalendar"
cd "C:/Claude(바이브코딩)/Study/task-share-manager/static/vendor"
curl -L -o bootstrap/bootstrap.min.css https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css
curl -L -o bootstrap/bootstrap.bundle.min.js https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/js/bootstrap.bundle.min.js
curl -L -o fullcalendar/index.global.min.js https://cdn.jsdelivr.net/npm/fullcalendar@6.1.15/index.global.min.js
```

- [ ] **Step 2: 파일 크기로 다운로드 성공 확인**

```bash
ls -la "C:/Claude(바이브코딩)/Study/task-share-manager/static/vendor/bootstrap" \
       "C:/Claude(바이브코딩)/Study/task-share-manager/static/vendor/fullcalendar"
```

Expected: 세 파일 모두 0바이트가 아님 (bootstrap.min.css 약 200KB+, bundle.min.js 약 80KB+, fullcalendar 약 500KB+)

- [ ] **Step 3: Commit**

```bash
git add task-share-manager/static/vendor
git commit -m "chore: Bootstrap, FullCalendar 정적 자산 로컬 번들 추가"
```

---

## Task 8: 로그인 화면 + 기본 레이아웃

**Files:**
- Create: `task-share-manager/templates/base.html`
- Create: `task-share-manager/templates/login.html`
- Create: `task-share-manager/static/css/style.css`
- Create: `task-share-manager/static/js/auth.js`
- Modify: `task-share-manager/app.py`

**Interfaces:**
- Consumes: `/api/login`, `/api/logout`, `/api/me` (Task 4)
- Produces: 로그인 성공 시 `/dashboard`로 이동. `static/js/auth.js`의 `requireLogin()` 함수 — Task 9, 11, 12에서 페이지 진입 시 재사용

- [ ] **Step 1: base.html 작성**

`task-share-manager/templates/base.html`:
```html
<!doctype html>
<html lang="ko">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>{% block title %}업무 공유 매니저{% endblock %}</title>
  <link rel="stylesheet" href="{{ url_for('static', filename='vendor/bootstrap/bootstrap.min.css') }}">
  <link rel="stylesheet" href="{{ url_for('static', filename='css/style.css') }}">
</head>
<body>
  {% block nav %}
  <nav class="navbar navbar-expand-lg navbar-dark bg-dark px-3">
    <a class="navbar-brand" href="{{ url_for('dashboard_page') }}">업무 공유 매니저</a>
    <div class="d-flex align-items-center gap-3 ms-auto">
      <a class="nav-link text-light" href="{{ url_for('dashboard_page') }}">대시보드</a>
      <a class="nav-link text-light" href="{{ url_for('calendar_page') }}">달력</a>
      <span id="current-user-name" class="text-light"></span>
      <button id="logout-btn" class="btn btn-outline-light btn-sm">로그아웃</button>
    </div>
  </nav>
  {% endblock %}
  <div class="container py-4">
    {% block content %}{% endblock %}
  </div>
  <script src="{{ url_for('static', filename='vendor/bootstrap/bootstrap.bundle.min.js') }}"></script>
  <script src="{{ url_for('static', filename='js/auth.js') }}"></script>
  {% block scripts %}{% endblock %}
</body>
</html>
```

- [ ] **Step 2: login.html 작성 (nav 없이 단독 페이지)**

`task-share-manager/templates/login.html`:
```html
<!doctype html>
<html lang="ko">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>로그인 - 업무 공유 매니저</title>
  <link rel="stylesheet" href="{{ url_for('static', filename='vendor/bootstrap/bootstrap.min.css') }}">
  <link rel="stylesheet" href="{{ url_for('static', filename='css/style.css') }}">
</head>
<body class="bg-light">
  <div class="d-flex justify-content-center align-items-center vh-100">
    <div class="card p-4 shadow-sm" style="width: 22rem;">
      <h4 class="mb-3 text-center">업무 공유 매니저</h4>
      <div id="login-error" class="alert alert-danger d-none"></div>
      <form id="login-form">
        <div class="mb-3">
          <label class="form-label">사번</label>
          <input type="text" class="form-control" id="emp_no" required>
        </div>
        <div class="mb-3">
          <label class="form-label">비밀번호</label>
          <input type="password" class="form-control" id="password" required>
        </div>
        <button type="submit" class="btn btn-primary w-100">로그인</button>
      </form>
    </div>
  </div>
  <script>
    document.getElementById("login-form").addEventListener("submit", async (e) => {
      e.preventDefault();
      const emp_no = document.getElementById("emp_no").value;
      const password = document.getElementById("password").value;
      const resp = await fetch("/api/login", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ emp_no, password }),
      });
      if (resp.ok) {
        window.location.href = "/dashboard";
      } else {
        const data = await resp.json();
        const errBox = document.getElementById("login-error");
        errBox.textContent = data.error || "로그인에 실패했습니다";
        errBox.classList.remove("d-none");
      }
    });
  </script>
</body>
</html>
```

- [ ] **Step 3: static/css/style.css 작성 (상태별 색상 정의 — Task 9, 11에서 사용)**

`task-share-manager/static/css/style.css`:
```css
.status-진행중 { border-left: 4px solid #0d6efd; }
.status-완료 { border-left: 4px solid #198754; opacity: 0.75; }
.status-취소 { border-left: 4px solid #6c757d; opacity: 0.5; text-decoration: line-through; }

.fc-event.status-진행중 { background-color: #0d6efd; border-color: #0d6efd; }
.fc-event.status-완료 { background-color: #198754; border-color: #198754; }
.fc-event.status-취소 { background-color: #6c757d; border-color: #6c757d; }
```

- [ ] **Step 4: static/js/auth.js 작성 (공용 로그인 체크 + 로그아웃 처리)**

`task-share-manager/static/js/auth.js`:
```javascript
async function requireLogin() {
  const resp = await fetch("/api/me");
  if (!resp.ok) {
    window.location.href = "/login";
    return null;
  }
  const user = await resp.json();
  const nameEl = document.getElementById("current-user-name");
  if (nameEl) nameEl.textContent = `${user.name} (${user.dept || "-"})`;
  return user;
}

document.addEventListener("DOMContentLoaded", () => {
  const logoutBtn = document.getElementById("logout-btn");
  if (logoutBtn) {
    logoutBtn.addEventListener("click", async () => {
      await fetch("/api/logout", { method: "POST" });
      window.location.href = "/login";
    });
  }
});
```

- [ ] **Step 5: app.py에 페이지 라우트 추가**

`task-share-manager/app.py`의 `create_app` 함수 안, `/health` 라우트 정의 다음에 추가:
```python
    from flask import redirect, render_template, session, url_for

    @app.route("/")
    def index():
        return redirect(url_for("dashboard_page"))

    @app.route("/login")
    def login_page():
        return render_template("login.html")

    @app.route("/dashboard")
    def dashboard_page():
        return render_template("dashboard.html")

    @app.route("/calendar")
    def calendar_page():
        return render_template("calendar.html")
```

> `dashboard.html`, `calendar.html`은 Task 9, 11에서 작성한다. 지금은 임시로 빈 템플릿을 만들어 라우팅만 확인한다.

- [ ] **Step 6: 임시 템플릿 생성 (Task 9, 11에서 실제 내용으로 교체)**

```bash
echo '{% extends "base.html" %}{% block content %}<p>대시보드 준비 중</p>{% endblock %}' > "C:/Claude(바이브코딩)/Study/task-share-manager/templates/dashboard.html"
echo '{% extends "base.html" %}{% block content %}<p>달력 준비 중</p>{% endblock %}' > "C:/Claude(바이브코딩)/Study/task-share-manager/templates/calendar.html"
```

- [ ] **Step 7: 서버 실행 후 브라우저로 로그인 흐름 수동 확인**

```bash
cd "C:/Claude(바이브코딩)/Study/task-share-manager"
venv/Scripts/python db/seed.py
venv/Scripts/python app.py
```

브라우저에서 `http://localhost:5050/login` 접속 → 사번 `E001`, 비밀번호 `1234`로 로그인 → `/dashboard`로 리다이렉트되고 상단에 "김민준 (정보전략파트)"가 표시되는지 확인. 로그아웃 버튼 클릭 시 `/login`으로 돌아가는지 확인.

- [ ] **Step 8: Commit**

```bash
git add task-share-manager/templates task-share-manager/static/css task-share-manager/static/js/auth.js task-share-manager/app.py
git commit -m "feat: 로그인 화면, 기본 레이아웃, 페이지 라우트 추가"
```

---

## Task 9: 대시보드 UI — 업무 목록/필터/등록·수정 모달

**Files:**
- Modify: `task-share-manager/templates/dashboard.html`
- Create: `task-share-manager/static/js/dashboard.js`

**Interfaces:**
- Consumes: `/api/tasks*`, `/api/categories` (Task 5, 6), `requireLogin()` (Task 8)
- Produces: 전역 함수 `loadTasks()`, `openTaskModal(taskId=null)` — Task 12에서 상태 요약 갱신 후 재사용

- [ ] **Step 1: dashboard.html 작성**

`task-share-manager/templates/dashboard.html`:
```html
{% extends "base.html" %}
{% block content %}
<div class="d-flex justify-content-between align-items-center mb-3">
  <div class="d-flex gap-2">
    <select id="category-filter" class="form-select" style="width: auto;">
      <option value="">전체 카테고리</option>
    </select>
    <select id="status-filter" class="form-select" style="width: auto;">
      <option value="">전체 상태</option>
      <option value="진행중">진행중</option>
      <option value="완료">완료</option>
      <option value="취소">취소</option>
    </select>
  </div>
  <div class="d-flex gap-2">
    <button id="new-category-btn" class="btn btn-outline-secondary">카테고리 관리</button>
    <button id="new-task-btn" class="btn btn-primary">업무 등록</button>
  </div>
</div>

<div id="task-list" class="row g-3"></div>

<!-- 업무 등록/수정 모달 -->
<div class="modal fade" id="task-modal" tabindex="-1">
  <div class="modal-dialog">
    <div class="modal-content">
      <form id="task-form">
        <div class="modal-header">
          <h5 class="modal-title">업무 등록</h5>
          <button type="button" class="btn-close" data-bs-dismiss="modal"></button>
        </div>
        <div class="modal-body">
          <input type="hidden" id="task-id">
          <div class="mb-2">
            <label class="form-label">제목</label>
            <input type="text" id="task-title" class="form-control" required>
          </div>
          <div class="mb-2">
            <label class="form-label">내용</label>
            <textarea id="task-content" class="form-control"></textarea>
          </div>
          <div class="mb-2">
            <label class="form-label">카테고리</label>
            <select id="task-category" class="form-select"></select>
          </div>
          <div class="mb-2">
            <label class="form-label">담당자 ID</label>
            <input type="number" id="task-assignee" class="form-control" required>
          </div>
          <div class="row">
            <div class="col mb-2">
              <label class="form-label">시작일</label>
              <input type="date" id="task-start" class="form-control">
            </div>
            <div class="col mb-2">
              <label class="form-label">종료일</label>
              <input type="date" id="task-end" class="form-control">
            </div>
          </div>
          <div class="mb-2">
            <label class="form-label">상태</label>
            <select id="task-status" class="form-select">
              <option value="진행중">진행중</option>
              <option value="완료">완료</option>
              <option value="취소">취소</option>
            </select>
          </div>
          <div id="task-form-error" class="alert alert-danger d-none"></div>
        </div>
        <div class="modal-footer">
          <button type="submit" class="btn btn-primary">저장</button>
        </div>
      </form>
    </div>
  </div>
</div>
{% endblock %}
{% block scripts %}
<script src="{{ url_for('static', filename='js/dashboard.js') }}"></script>
{% endblock %}
```

- [ ] **Step 2: dashboard.js 작성**

`task-share-manager/static/js/dashboard.js`:
```javascript
let taskModal;

async function loadCategories() {
  const resp = await fetch("/api/categories");
  const categories = await resp.json();
  const filterSelect = document.getElementById("category-filter");
  const formSelect = document.getElementById("task-category");
  categories.forEach((c) => {
    const opt1 = new Option(c.name, c.id);
    filterSelect.add(opt1);
    const opt2 = new Option(c.name, c.id);
    formSelect.add(opt2);
  });
  return categories;
}

function statusBadgeClass(status) {
  return { "진행중": "primary", "완료": "success", "취소": "secondary" }[status] || "secondary";
}

function renderTasks(tasks) {
  const list = document.getElementById("task-list");
  list.innerHTML = "";
  tasks.forEach((t) => {
    const col = document.createElement("div");
    col.className = "col-md-4";
    col.innerHTML = `
      <div class="card status-${t.status}">
        <div class="card-body">
          <h5 class="card-title">${t.title}</h5>
          <span class="badge bg-${statusBadgeClass(t.status)}">${t.status}</span>
          <p class="card-text mt-2">${t.content || ""}</p>
          <p class="text-muted small">${t.start_date || "-"} ~ ${t.end_date || "-"}</p>
          <div class="d-flex gap-2">
            <button class="btn btn-sm btn-outline-primary edit-btn" data-id="${t.id}">수정</button>
            <button class="btn btn-sm btn-outline-success complete-btn" data-id="${t.id}">완료 처리</button>
            <button class="btn btn-sm btn-outline-danger cancel-btn" data-id="${t.id}">취소</button>
          </div>
        </div>
      </div>`;
    list.appendChild(col);
  });

  list.querySelectorAll(".edit-btn").forEach((btn) =>
    btn.addEventListener("click", () => openTaskModal(btn.dataset.id))
  );
  list.querySelectorAll(".complete-btn").forEach((btn) =>
    btn.addEventListener("click", () => changeStatus(btn.dataset.id, "완료"))
  );
  list.querySelectorAll(".cancel-btn").forEach((btn) =>
    btn.addEventListener("click", () => changeStatus(btn.dataset.id, "취소"))
  );
}

async function loadTasks() {
  const category = document.getElementById("category-filter").value;
  const status = document.getElementById("status-filter").value;
  const params = new URLSearchParams();
  if (category) params.set("category_id", category);
  if (status) params.set("status", status);

  const resp = await fetch(`/api/tasks?${params.toString()}`);
  const tasks = await resp.json();
  renderTasks(tasks);
}

async function changeStatus(taskId, status) {
  await fetch(`/api/tasks/${taskId}/status`, {
    method: "PATCH",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ status }),
  });
  loadTasks();
}

async function openTaskModal(taskId) {
  document.getElementById("task-form-error").classList.add("d-none");
  document.getElementById("task-form").reset();
  document.getElementById("task-id").value = taskId || "";

  if (taskId) {
    const resp = await fetch(`/api/tasks/${taskId}`);
    const t = await resp.json();
    document.getElementById("task-title").value = t.title;
    document.getElementById("task-content").value = t.content || "";
    document.getElementById("task-category").value = t.category_id || "";
    document.getElementById("task-assignee").value = t.assignee_id;
    document.getElementById("task-start").value = t.start_date || "";
    document.getElementById("task-end").value = t.end_date || "";
    document.getElementById("task-status").value = t.status;
  }
  taskModal.show();
}

async function submitTaskForm(e) {
  e.preventDefault();
  const taskId = document.getElementById("task-id").value;
  const payload = {
    title: document.getElementById("task-title").value,
    content: document.getElementById("task-content").value,
    category_id: document.getElementById("task-category").value || null,
    assignee_id: Number(document.getElementById("task-assignee").value),
    start_date: document.getElementById("task-start").value || null,
    end_date: document.getElementById("task-end").value || null,
    status: document.getElementById("task-status").value,
  };

  const url = taskId ? `/api/tasks/${taskId}` : "/api/tasks";
  const method = taskId ? "PUT" : "POST";
  const resp = await fetch(url, {
    method,
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });

  if (resp.ok) {
    taskModal.hide();
    loadTasks();
  } else {
    const data = await resp.json();
    const errBox = document.getElementById("task-form-error");
    errBox.textContent = data.error || "저장에 실패했습니다";
    errBox.classList.remove("d-none");
  }
}

document.addEventListener("DOMContentLoaded", async () => {
  const user = await requireLogin();
  if (!user) return;

  taskModal = new bootstrap.Modal(document.getElementById("task-modal"));
  await loadCategories();
  await loadTasks();

  document.getElementById("category-filter").addEventListener("change", loadTasks);
  document.getElementById("status-filter").addEventListener("change", loadTasks);
  document.getElementById("new-task-btn").addEventListener("click", () => openTaskModal(null));
  document.getElementById("task-form").addEventListener("submit", submitTaskForm);
});
```

- [ ] **Step 3: 브라우저로 수동 확인**

서버 실행 후(`venv/Scripts/python app.py`) `http://localhost:5050/dashboard`에서:
1. 카테고리 필터 드롭다운에 시드 카테고리("일반 업무", "수술")가 나오는지 확인
2. "업무 등록" 클릭 → 제목/담당자ID 입력 → 저장 → 목록에 카드가 추가되는지 확인
3. "완료 처리" 클릭 → 카드가 회색 처리(opacity)되고 배지가 "완료"로 바뀌는지 확인
4. 제목을 비운 채 저장 시도 → 에러 메시지가 모달에 표시되는지 확인 (백엔드 400 응답 확인)

- [ ] **Step 4: Commit**

```bash
git add task-share-manager/templates/dashboard.html task-share-manager/static/js/dashboard.js
git commit -m "feat: 대시보드 업무 목록/필터/등록수정 모달 UI 추가"
```

---

## Task 10: 카테고리 관리 화면 (생성 + 공유 대상자 지정)

**Files:**
- Create: `task-share-manager/templates/category_modal.html` (dashboard.html에 include)
- Modify: `task-share-manager/templates/dashboard.html`
- Modify: `task-share-manager/static/js/dashboard.js`

**Interfaces:**
- Consumes: `/api/categories*`, `/api/categories/<id>/shares` (Task 5)

- [ ] **Step 1: dashboard.html에 카테고리 관리 모달 추가**

`task-share-manager/templates/dashboard.html`의 `{% endblock %}` (content 블록) 바로 앞에 추가:
```html
<!-- 카테고리 관리 모달 -->
<div class="modal fade" id="category-modal" tabindex="-1">
  <div class="modal-dialog">
    <div class="modal-content">
      <div class="modal-header">
        <h5 class="modal-title">카테고리 관리</h5>
        <button type="button" class="btn-close" data-bs-dismiss="modal"></button>
      </div>
      <div class="modal-body">
        <form id="new-category-form" class="d-flex gap-2 mb-3">
          <input type="text" id="new-category-name" class="form-control" placeholder="새 카테고리 이름" required>
          <button type="submit" class="btn btn-primary">추가</button>
        </form>
        <ul id="category-manage-list" class="list-group"></ul>

        <hr>
        <h6>공유 대상자 지정 (사용자 ID 입력)</h6>
        <div class="d-flex gap-2">
          <select id="share-category-select" class="form-select"></select>
          <input type="number" id="share-user-id" class="form-control" placeholder="사용자 ID">
          <button id="add-share-btn" class="btn btn-outline-primary">공유 등록</button>
        </div>
      </div>
    </div>
  </div>
</div>
```

- [ ] **Step 2: dashboard.js에 카테고리 관리 로직 추가**

`task-share-manager/static/js/dashboard.js` 파일 끝에 추가:
```javascript
let categoryModal;

function renderCategoryManageList(categories) {
  const list = document.getElementById("category-manage-list");
  const shareSelect = document.getElementById("share-category-select");
  list.innerHTML = "";
  shareSelect.innerHTML = "";
  categories.forEach((c) => {
    const li = document.createElement("li");
    li.className = "list-group-item";
    li.textContent = `${c.name} — ${c.description || ""}`;
    list.appendChild(li);
    shareSelect.add(new Option(c.name, c.id));
  });
}

document.getElementById("new-category-btn")?.addEventListener("click", async () => {
  if (!categoryModal) categoryModal = new bootstrap.Modal(document.getElementById("category-modal"));
  const resp = await fetch("/api/categories");
  renderCategoryManageList(await resp.json());
  categoryModal.show();
});

document.getElementById("new-category-form")?.addEventListener("submit", async (e) => {
  e.preventDefault();
  const name = document.getElementById("new-category-name").value;
  await fetch("/api/categories", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ name }),
  });
  document.getElementById("new-category-name").value = "";
  const resp = await fetch("/api/categories");
  const categories = await resp.json();
  renderCategoryManageList(categories);
  await loadCategories();
});

document.getElementById("add-share-btn")?.addEventListener("click", async () => {
  const categoryId = document.getElementById("share-category-select").value;
  const userId = Number(document.getElementById("share-user-id").value);
  if (!categoryId || !userId) return;
  const resp = await fetch(`/api/categories/${categoryId}/shares`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ user_id: userId }),
  });
  if (resp.ok) {
    document.getElementById("share-user-id").value = "";
    alert("공유 대상자가 등록되었습니다");
  } else {
    const data = await resp.json();
    alert(data.error || "등록 실패");
  }
});
```

- [ ] **Step 3: 브라우저로 수동 확인**

`http://localhost:5050/dashboard`에서 "카테고리 관리" 클릭 → 새 카테고리 이름 입력 후 "추가" → 목록에 반영되고 대시보드 필터 드롭다운에도 반영되는지 확인. 공유 대상자 지정에서 다른 시드 사용자 ID(예: E002의 id)를 입력해 "공유 등록" 클릭 → alert로 성공 메시지 확인.

- [ ] **Step 4: Commit**

```bash
git add task-share-manager/templates/dashboard.html task-share-manager/static/js/dashboard.js
git commit -m "feat: 카테고리 생성 및 공유 대상자 지정 UI 추가"
```

---

## Task 11: 달력 시각화 (FullCalendar) + 오늘의 주요일정

**Files:**
- Modify: `task-share-manager/templates/calendar.html`
- Modify: `task-share-manager/templates/dashboard.html`
- Create: `task-share-manager/static/js/calendar.js`
- Modify: `task-share-manager/static/js/dashboard.js`

**Interfaces:**
- Consumes: `/api/tasks`, `/api/categories` (Task 6, 5)

- [ ] **Step 1: calendar.html 작성**

`task-share-manager/templates/calendar.html`:
```html
{% extends "base.html" %}
{% block content %}
<div id="calendar"></div>
{% endblock %}
{% block scripts %}
<script src="{{ url_for('static', filename='vendor/fullcalendar/index.global.min.js') }}"></script>
<script src="{{ url_for('static', filename='js/calendar.js') }}"></script>
{% endblock %}
```

- [ ] **Step 2: calendar.js 작성 (달력 렌더링, 이벤트 클릭, 날짜 클릭)**

`task-share-manager/static/js/calendar.js`:
```javascript
document.addEventListener("DOMContentLoaded", async () => {
  const user = await requireLogin();
  if (!user) return;

  const resp = await fetch("/api/tasks");
  const tasks = await resp.json();

  const events = tasks.map((t) => ({
    id: t.id,
    title: t.title,
    start: t.start_date,
    end: t.end_date,
    classNames: [`status-${t.status}`],
  }));

  const calendarEl = document.getElementById("calendar");
  const calendar = new FullCalendar.Calendar(calendarEl, {
    initialView: "dayGridMonth",
    headerToolbar: {
      left: "prev,next today",
      center: "title",
      right: "dayGridMonth,timeGridWeek",
    },
    events,
    eventClick: (info) => {
      window.location.href = `/dashboard?edit=${info.event.id}`;
    },
    dateClick: (info) => {
      window.location.href = `/dashboard?new_date=${info.dateStr}`;
    },
  });
  calendar.render();
});
```

- [ ] **Step 3: dashboard.js에 URL 파라미터 처리 추가 (달력에서 넘어온 edit/new_date 반영)**

`task-share-manager/static/js/dashboard.js`의 `DOMContentLoaded` 핸들러 안, `await loadTasks();` 다음 줄에 추가:
```javascript
  const params = new URLSearchParams(window.location.search);
  if (params.get("edit")) {
    openTaskModal(params.get("edit"));
  } else if (params.get("new_date")) {
    await openTaskModal(null);
    document.getElementById("task-start").value = params.get("new_date");
    document.getElementById("task-end").value = params.get("new_date");
  }
```

- [ ] **Step 4: 오늘의 주요일정 위젯 추가 — dashboard.html 상단**

`task-share-manager/templates/dashboard.html`의 `{% block content %}` 바로 다음 줄에 추가:
```html
<div id="today-widget" class="card mb-3 border-primary">
  <div class="card-header bg-primary text-white">오늘의 주요일정</div>
  <ul id="today-list" class="list-group list-group-flush"></ul>
</div>
```

- [ ] **Step 5: dashboard.js에 오늘의 주요일정 로직 추가**

`task-share-manager/static/js/dashboard.js` 파일 끝에 추가:
```javascript
function todayStr() {
  return new Date().toISOString().slice(0, 10);
}

async function loadTodayWidget() {
  const resp = await fetch("/api/tasks");
  const tasks = await resp.json();
  const today = todayStr();

  const todays = tasks.filter(
    (t) => t.status !== "취소" && (t.start_date === today || t.end_date === today)
  );

  const priorityCategoryNames = ["수술", "시술", "치료"];
  todays.sort((a, b) => {
    const aPriority = priorityCategoryNames.includes(a.category_name) ? 0 : 1;
    const bPriority = priorityCategoryNames.includes(b.category_name) ? 0 : 1;
    return aPriority - bPriority;
  });

  const list = document.getElementById("today-list");
  list.innerHTML = "";
  if (todays.length === 0) {
    list.innerHTML = '<li class="list-group-item text-muted">오늘 시작/마감 업무가 없습니다</li>';
    return;
  }
  todays.forEach((t) => {
    const li = document.createElement("li");
    li.className = "list-group-item";
    li.textContent = `[${t.status}] ${t.title} (${t.start_date} ~ ${t.end_date || "-"})`;
    list.appendChild(li);
  });
}
```

`DOMContentLoaded` 핸들러 안, `await loadTasks();` 다음 줄에 `await loadTodayWidget();` 추가.

> 주의: `/api/tasks` 응답에는 `category_name`이 없다(현재 tasks 테이블에는 category_id만 있음). Step 6에서 tasks.py의 list_tasks를 JOIN으로 보강한다.

- [ ] **Step 6: tasks.py의 list_tasks에 카테고리 이름 JOIN 추가**

`task-share-manager/tasks.py`의 `list_tasks` 함수를 다음으로 교체:
```python
@tasks_bp.route("", methods=["GET"])
@login_required
def list_tasks():
    db = get_db()
    user_id = session["user_id"]
    status_filter = request.args.get("status")
    category_filter = request.args.get("category_id")

    rows = db.execute(
        """SELECT t.*, c.name AS category_name
           FROM tasks t LEFT JOIN categories c ON c.id = t.category_id"""
    ).fetchall()
    visible = [dict(r) for r in rows if can_view_task(db, r, user_id)]
    if status_filter:
        visible = [t for t in visible if t["status"] == status_filter]
    if category_filter:
        visible = [t for t in visible if str(t["category_id"]) == str(category_filter)]
    return jsonify(visible)
```

- [ ] **Step 7: 기존 테스트가 여전히 통과하는지 확인**

```bash
cd "C:/Claude(바이브코딩)/Study/task-share-manager"
venv/Scripts/pytest tests/ -v
```

Expected: PASS (JOIN 추가로 인한 회귀 없음 — `can_view_task`는 `task["assignee_id"]`, `task["category_id"]`, `task["id"]`만 사용하므로 sqlite3.Row에 컬럼이 추가돼도 영향 없음)

- [ ] **Step 8: 브라우저로 수동 확인**

시드 데이터의 "수술 준비" 업무의 `start_date`를 오늘 날짜로 임시 수정 후(대시보드에서 수정) 대시보드 상단 "오늘의 주요일정"에 나타나는지, "수술" 카테고리 업무가 상단에 우선 노출되는지 확인. `/calendar` 페이지에서 월간 달력에 업무들이 상태별 색상으로 표시되는지, 날짜 클릭 시 `/dashboard?new_date=...`로 이동해 등록 모달이 해당 날짜로 열리는지 확인.

- [ ] **Step 9: Commit**

```bash
git add task-share-manager/templates task-share-manager/static/js task-share-manager/tasks.py
git commit -m "feat: FullCalendar 달력, 오늘의 주요일정 위젯 추가"
```

---

## Task 12: 담당자별 업무 현황 요약

**Files:**
- Modify: `task-share-manager/templates/dashboard.html`
- Modify: `task-share-manager/static/js/dashboard.js`

**Interfaces:**
- Consumes: `/api/tasks` (Task 6, 11에서 이미 category_name 포함하도록 확장됨)

- [ ] **Step 1: dashboard.html에 현황 요약 카드 추가**

`task-share-manager/templates/dashboard.html`의 `today-widget` 다음 줄에 추가:
```html
<div id="status-summary" class="row g-2 mb-3"></div>
```

- [ ] **Step 2: dashboard.js에 요약 렌더링 함수 추가**

`task-share-manager/static/js/dashboard.js` 파일 끝에 추가:
```javascript
async function loadStatusSummary() {
  const resp = await fetch("/api/tasks");
  const tasks = await resp.json();

  const counts = { "진행중": 0, "완료": 0, "취소": 0 };
  tasks.forEach((t) => { counts[t.status] = (counts[t.status] || 0) + 1; });
  const total = tasks.length;
  const progress = total > 0 ? Math.round((counts["완료"] / total) * 100) : 0;

  const el = document.getElementById("status-summary");
  el.innerHTML = `
    <div class="col-auto"><span class="badge bg-primary">진행중 ${counts["진행중"]}</span></div>
    <div class="col-auto"><span class="badge bg-success">완료 ${counts["완료"]}</span></div>
    <div class="col-auto"><span class="badge bg-secondary">취소 ${counts["취소"]}</span></div>
    <div class="col-auto"><span class="badge bg-info text-dark">진행률 ${progress}%</span></div>
  `;
}
```

`DOMContentLoaded` 핸들러 안, `await loadTodayWidget();` 다음 줄에 `await loadStatusSummary();` 추가. 그리고 `changeStatus()`, `submitTaskForm()` 함수의 성공 처리부(`loadTasks();` 호출 부분)에도 `loadStatusSummary();`를 함께 호출하도록 추가한다.

- [ ] **Step 3: 브라우저로 수동 확인**

대시보드에서 업무 하나를 "완료 처리" → 상단 요약 배지의 "완료" 카운트와 "진행률"이 즉시 갱신되는지 확인.

- [ ] **Step 4: Commit**

```bash
git add task-share-manager/templates/dashboard.html task-share-manager/static/js/dashboard.js
git commit -m "feat: 담당자별 업무 현황 요약(진행중/완료/취소/진행률) 위젯 추가"
```

---

## Task 13: README (실행법, 폐쇄망 배포 가이드, 시나리오 테스트 체크리스트)

**Files:**
- Create: `task-share-manager/README.md`

**Interfaces:**
- Consumes: 전체 프로젝트 (Task 1~12)

- [ ] **Step 1: README.md 작성**

`task-share-manager/README.md`:
```markdown
# 업무 공유 매니저 (Task Share Manager)

병원 내부망(폐쇄망) 배포용 업무 공유·관리 웹앱. Flask + SQLite + Bootstrap + FullCalendar.

## 개발 환경 실행

\`\`\`bash
python -m venv venv
venv/Scripts/pip install -r requirements.txt   # Windows
# source venv/bin/activate && pip install -r requirements.txt   # macOS/Linux

venv/Scripts/python db/seed.py     # DB 초기화 + 더미 데이터(사용자 3, 카테고리 2, 업무 5) 삽입
venv/Scripts/python app.py         # http://localhost:5050
\`\`\`

시드 계정: 사번 `E001`/`E002`/`E003`, 비밀번호 공통 `1234`.

## 테스트

\`\`\`bash
venv/Scripts/pytest tests/ -v
\`\`\`

## 폐쇄망 배포 가이드

### 1. 오프라인 파이썬 패키지 준비 (인터넷 되는 PC에서)

\`\`\`bash
pip download -r requirements.txt -d offline_packages
\`\`\`

`offline_packages/` 폴더와 프로젝트 전체(정적 자산 `static/vendor` 포함)를 USB 등으로 내부망에 반입한다.

### 2. 내부망 서버에서 설치

\`\`\`bash
python -m venv venv
venv/Scripts/pip install --no-index --find-links=offline_packages -r requirements.txt
venv/Scripts/python db/seed.py
\`\`\`

### 3. 서버 자동 시작 (Windows)

작업 스케줄러에 "로그온 시 시작" 트리거로 아래 작업 등록:
- 프로그램: `<프로젝트경로>\venv\Scripts\python.exe`
- 인수: `<프로젝트경로>\app.py`
- 시작 위치: `<프로젝트경로>`

또는 NSSM 같은 도구로 Windows 서비스 등록도 가능하다.

### 4. SQLite 백업

`tasks.db`는 단일 파일이므로, 서버가 내려간 상태(또는 파일 잠금이 없는 새벽 시간대)에 아래처럼 주기적으로 복사하면 된다.

\`\`\`bash
copy tasks.db backup\tasks_%date:~0,4%%date:~5,2%%date:~8,2%.db
\`\`\`

작업 스케줄러로 매일 자정 실행하도록 등록해 백업을 자동화할 수 있다.

## 시나리오 테스트 체크리스트

- [ ] 사번/비밀번호로 로그인 → 대시보드로 이동하고 사용자 이름이 표시된다
- [ ] 카테고리 관리에서 새 카테고리를 생성하면 필터/등록 모달 드롭다운에 즉시 반영된다
- [ ] 업무를 등록하면 목록에 카드로 나타나고 상태 배지가 "진행중"이다
- [ ] 다른 계정으로 로그인해 방금 등록한 업무를 조회하면 403(조회 권한 없음)이 반환된다
- [ ] 카테고리 관리에서 해당 카테고리를 다른 사용자에게 공유하면, 그 사용자는 업무 목록/달력에서 해당 카테고리 업무를 볼 수 있다
- [ ] 업무를 개별 공유(공유 대상자 등록)하면, 대상자가 목록/달력에서 해당 업무를 볼 수 있다
- [ ] "완료 처리" 클릭 시 카드가 회색으로 바뀌고 상단 현황 요약의 완료 건수·진행률이 갱신된다
- [ ] "취소" 클릭 시 취소선이 표시되고 현황 요약에 반영된다
- [ ] 제목을 비운 채 저장하면 400 에러 메시지가 모달에 표시된다
- [ ] 종료일을 시작일보다 이전으로 입력하면 저장이 거부된다
- [ ] `/calendar`에서 업무가 시작일~종료일 구간의 이벤트로, 상태별 색상으로 표시된다
- [ ] 달력의 날짜를 클릭하면 그 날짜가 기본값으로 채워진 등록 모달이 열린다
- [ ] 오늘 시작/마감인 업무가 대시보드 상단 "오늘의 주요일정"에 노출되고, "수술/시술/치료" 카테고리 업무가 우선 정렬된다
- [ ] 로그아웃 후 `/dashboard`에 직접 접근하면 `/login`으로 리다이렉트된다 (미로그인 시 `/api/*` 401 → `requireLogin()`이 이동시킴)
\`\`\`

- [ ] **Step 2: 위 체크리스트를 실제로 한 번 수행**

`venv/Scripts/python app.py`로 서버를 띄운 상태에서 체크리스트 14개 항목을 브라우저로 직접 확인하고, 실패하는 항목이 있으면 해당 Task로 돌아가 수정한다.

- [ ] **Step 3: 전체 테스트 스위트 최종 실행**

```bash
venv/Scripts/pytest tests/ -v
```

Expected: 전체 PASS

- [ ] **Step 4: Commit**

```bash
git add task-share-manager/README.md
git commit -m "docs: README, 폐쇄망 배포 가이드, 시나리오 테스트 체크리스트 추가"
```
