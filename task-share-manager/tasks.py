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
