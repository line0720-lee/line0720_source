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
