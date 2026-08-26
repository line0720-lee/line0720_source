import os

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
SECRET_KEY = "dev-secret-change-in-production"
DATABASE = os.path.join(BASE_DIR, "tasks.db")
