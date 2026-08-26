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

    already_seeded = conn.execute("SELECT 1 FROM categories LIMIT 1").fetchone() is not None
    if already_seeded:
        conn.close()
        return

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
