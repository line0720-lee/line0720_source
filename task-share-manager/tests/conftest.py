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
