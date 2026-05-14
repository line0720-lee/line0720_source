import os
import sqlite3
from datetime import date
from contextlib import contextmanager

# 로컬: quotes.db / 클라우드(Render): /data/quotes.db
DB_PATH = os.environ.get("DB_PATH", "quotes.db")

VENDOR_NAMES = ["KCC", "LG", "영림", "예림"]


@contextmanager
def get_db():
    conn = sqlite3.connect(DB_PATH)
    conn.row_factory = sqlite3.Row
    conn.execute("PRAGMA foreign_keys = ON")
    try:
        yield conn
        conn.commit()
    except Exception:
        conn.rollback()
        raise
    finally:
        conn.close()


def _safe_add_column(conn, table, column, definition):
    try:
        conn.execute(f"ALTER TABLE {table} ADD COLUMN {column} {definition}")
    except Exception:
        pass  # column already exists


def init_db():
    with get_db() as conn:
        conn.executescript("""
            CREATE TABLE IF NOT EXISTS settings (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                key TEXT UNIQUE NOT NULL,
                value TEXT
            );

            CREATE TABLE IF NOT EXISTS vendors (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT UNIQUE NOT NULL,
                created_at TEXT DEFAULT (date('now', 'localtime'))
            );

            CREATE TABLE IF NOT EXISTS items (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                vendor_id INTEGER REFERENCES vendors(id) ON DELETE CASCADE,
                name TEXT NOT NULL,
                size TEXT,
                unit_price INTEGER NOT NULL DEFAULT 0,
                remarks TEXT,
                created_at TEXT DEFAULT (date('now', 'localtime'))
            );

            CREATE TABLE IF NOT EXISTS quotations (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                vendor_id INTEGER REFERENCES vendors(id),
                vendor_name TEXT,
                quote_number TEXT UNIQUE NOT NULL,
                customer_company TEXT,
                customer_name TEXT,
                customer_phone TEXT,
                issue_date TEXT,
                valid_until TEXT,
                notes TEXT,
                supply_total INTEGER DEFAULT 0,
                vat INTEGER DEFAULT 0,
                grand_total INTEGER DEFAULT 0,
                pdf_path TEXT,
                created_at TEXT DEFAULT (datetime('now', 'localtime'))
            );

            CREATE TABLE IF NOT EXISTS quotation_items (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                quotation_id INTEGER NOT NULL,
                name TEXT NOT NULL,
                size TEXT,
                quantity INTEGER DEFAULT 1,
                unit_price INTEGER DEFAULT 0,
                amount INTEGER DEFAULT 0,
                remarks TEXT,
                FOREIGN KEY (quotation_id) REFERENCES quotations(id) ON DELETE CASCADE
            );
        """)

        # 기존 DB 마이그레이션 (컬럼이 없을 경우 추가)
        _safe_add_column(conn, "items", "vendor_id", "INTEGER REFERENCES vendors(id) ON DELETE CASCADE")
        _safe_add_column(conn, "quotations", "vendor_id", "INTEGER REFERENCES vendors(id)")
        _safe_add_column(conn, "quotations", "vendor_name", "TEXT")

        # 기본 업체 삽입
        for name in VENDOR_NAMES:
            conn.execute(
                "INSERT OR IGNORE INTO vendors(name) VALUES(?)", (name,)
            )

        # 기본 회사명 설정
        conn.execute(
            "INSERT INTO settings(key, value) VALUES('company_name', '샤시크루') "
            "ON CONFLICT(key) DO NOTHING"
        )


# ── Vendors ────────────────────────────────────────────────────────────────

def get_all_vendors():
    with get_db() as conn:
        return conn.execute("SELECT * FROM vendors ORDER BY id").fetchall()


def get_vendor(vendor_id):
    with get_db() as conn:
        return conn.execute("SELECT * FROM vendors WHERE id=?", (vendor_id,)).fetchone()


# ── Settings ──────────────────────────────────────────────────────────────

def get_setting(key, default=""):
    with get_db() as conn:
        row = conn.execute("SELECT value FROM settings WHERE key=?", (key,)).fetchone()
        return row["value"] if row else default


def get_all_settings():
    keys = ["company_name", "address", "phone", "fax", "manager", "pdf_save_path"]
    with get_db() as conn:
        rows = conn.execute("SELECT key, value FROM settings").fetchall()
        result = {k: "" for k in keys}
        result.update({r["key"]: r["value"] for r in rows})
        if not result["company_name"]:
            result["company_name"] = "샤시크루"
        return result


def save_settings(data: dict):
    with get_db() as conn:
        for key, value in data.items():
            conn.execute(
                "INSERT INTO settings(key, value) VALUES(?,?) "
                "ON CONFLICT(key) DO UPDATE SET value=excluded.value",
                (key, value)
            )


# ── Items ──────────────────────────────────────────────────────────────────

def get_all_items(vendor_id=None):
    with get_db() as conn:
        if vendor_id:
            return conn.execute(
                "SELECT i.*, v.name as vendor_name FROM items i "
                "LEFT JOIN vendors v ON i.vendor_id=v.id "
                "WHERE i.vendor_id=? ORDER BY i.name",
                (vendor_id,)
            ).fetchall()
        return conn.execute(
            "SELECT i.*, v.name as vendor_name FROM items i "
            "LEFT JOIN vendors v ON i.vendor_id=v.id ORDER BY v.id, i.name"
        ).fetchall()


def get_item(item_id):
    with get_db() as conn:
        return conn.execute(
            "SELECT i.*, v.name as vendor_name FROM items i "
            "LEFT JOIN vendors v ON i.vendor_id=v.id WHERE i.id=?",
            (item_id,)
        ).fetchone()


def create_item(vendor_id, name, size, unit_price, remarks):
    with get_db() as conn:
        cur = conn.execute(
            "INSERT INTO items(vendor_id, name, size, unit_price, remarks) VALUES(?,?,?,?,?)",
            (vendor_id, name, size or "", int(unit_price), remarks or "")
        )
        return cur.lastrowid


def update_item(item_id, vendor_id, name, size, unit_price, remarks):
    with get_db() as conn:
        conn.execute(
            "UPDATE items SET vendor_id=?, name=?, size=?, unit_price=?, remarks=? WHERE id=?",
            (vendor_id, name, size or "", int(unit_price), remarks or "", item_id)
        )


def delete_item(item_id):
    with get_db() as conn:
        conn.execute("DELETE FROM items WHERE id=?", (item_id,))


def get_items_count_by_vendor():
    with get_db() as conn:
        rows = conn.execute(
            "SELECT v.id, v.name, COUNT(i.id) as cnt "
            "FROM vendors v LEFT JOIN items i ON v.id=i.vendor_id "
            "GROUP BY v.id ORDER BY v.id"
        ).fetchall()
        return rows


# ── Quotations ─────────────────────────────────────────────────────────────

def _next_quote_number(vendor_name):
    today = date.today().strftime("%Y%m%d")
    prefix = f"Q-{today}-"
    with get_db() as conn:
        row = conn.execute(
            "SELECT quote_number FROM quotations WHERE quote_number LIKE ? ORDER BY quote_number DESC LIMIT 1",
            (prefix + "%",)
        ).fetchone()
    if row:
        seq = int(row["quote_number"].split("-")[-1]) + 1
    else:
        seq = 1
    return f"{prefix}{seq:03d}"


def create_quotation(vendor_id, vendor_name,
                     customer_company, customer_name, customer_phone,
                     issue_date, valid_until, notes,
                     supply_total, vat, grand_total, line_items):
    qnum = _next_quote_number(vendor_name)
    with get_db() as conn:
        cur = conn.execute(
            """INSERT INTO quotations
               (vendor_id, vendor_name, quote_number,
                customer_company, customer_name, customer_phone,
                issue_date, valid_until, notes, supply_total, vat, grand_total)
               VALUES(?,?,?,?,?,?,?,?,?,?,?,?)""",
            (vendor_id, vendor_name, qnum,
             customer_company, customer_name, customer_phone,
             issue_date, valid_until, notes, supply_total, vat, grand_total)
        )
        qid = cur.lastrowid
        for item in line_items:
            conn.execute(
                """INSERT INTO quotation_items
                   (quotation_id, name, size, quantity, unit_price, amount, remarks)
                   VALUES(?,?,?,?,?,?,?)""",
                (qid, item["name"], item.get("size", ""),
                 int(item["quantity"]), int(item["unit_price"]),
                 int(item["amount"]), item.get("remarks", ""))
            )
    return qid, qnum


def update_quotation_pdf(qid, pdf_path):
    with get_db() as conn:
        conn.execute("UPDATE quotations SET pdf_path=? WHERE id=?", (pdf_path, qid))


def get_quotation(qid):
    with get_db() as conn:
        q = conn.execute("SELECT * FROM quotations WHERE id=?", (qid,)).fetchone()
        if not q:
            return None, []
        items = conn.execute(
            "SELECT * FROM quotation_items WHERE quotation_id=? ORDER BY id",
            (qid,)
        ).fetchall()
        return q, items


def get_all_quotations(vendor_id=None):
    with get_db() as conn:
        if vendor_id:
            return conn.execute(
                "SELECT * FROM quotations WHERE vendor_id=? ORDER BY created_at DESC",
                (vendor_id,)
            ).fetchall()
        return conn.execute(
            "SELECT * FROM quotations ORDER BY created_at DESC"
        ).fetchall()


def get_quotation_stats():
    """업체별 통계: 견적 수, 합계금액, 이번달 합계"""
    today = date.today()
    this_month = today.strftime("%Y-%m")
    with get_db() as conn:
        rows = conn.execute(
            """SELECT v.id, v.name,
                  COUNT(q.id) as total_count,
                  COALESCE(SUM(q.grand_total), 0) as total_amount,
                  COALESCE(SUM(CASE WHEN q.created_at LIKE ? THEN q.grand_total ELSE 0 END), 0) as month_amount,
                  COALESCE(SUM(CASE WHEN q.created_at LIKE ? THEN 1 ELSE 0 END), 0) as month_count
               FROM vendors v
               LEFT JOIN quotations q ON v.id=q.vendor_id
               GROUP BY v.id ORDER BY v.id""",
            (this_month + "%", this_month + "%")
        ).fetchall()
        return rows


def delete_quotation(qid):
    with get_db() as conn:
        conn.execute("DELETE FROM quotations WHERE id=?", (qid,))
