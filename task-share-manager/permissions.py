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
