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
