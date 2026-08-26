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
