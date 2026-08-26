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
