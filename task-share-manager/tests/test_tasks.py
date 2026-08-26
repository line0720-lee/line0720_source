def _register_and_login(client, emp_no, password="pw1234"):
    client.post("/api/register", json={"emp_no": emp_no, "name": "사용자", "dept": "IT", "password": password})
    client.post("/api/login", json={"emp_no": emp_no, "password": password})
    return client.get("/api/me").get_json()


def test_create_and_get_own_task(client):
    me = _register_and_login(client, "T001")
    resp = client.post(
        "/api/tasks",
        json={"title": "제목", "assignee_id": me["id"], "start_date": "2026-09-01", "end_date": "2026-09-02"},
    )
    assert resp.status_code == 201
    task_id = resp.get_json()["id"]

    resp = client.get(f"/api/tasks/{task_id}")
    assert resp.status_code == 200
    assert resp.get_json()["status"] == "진행중"


def test_create_task_missing_title_fails(client):
    me = _register_and_login(client, "T002")
    resp = client.post("/api/tasks", json={"assignee_id": me["id"]})
    assert resp.status_code == 400


def test_create_task_end_before_start_fails(client):
    me = _register_and_login(client, "T003")
    resp = client.post(
        "/api/tasks",
        json={"title": "제목", "assignee_id": me["id"], "start_date": "2026-09-10", "end_date": "2026-09-01"},
    )
    assert resp.status_code == 400


def test_other_user_cannot_view_unshared_task(client):
    me = _register_and_login(client, "T004")
    created = client.post("/api/tasks", json={"title": "비공개 업무", "assignee_id": me["id"]}).get_json()

    client.post("/api/logout")
    _register_and_login(client, "T005")
    resp = client.get(f"/api/tasks/{created['id']}")
    assert resp.status_code == 403


def test_task_share_grants_visibility(client):
    me = _register_and_login(client, "T006")
    created = client.post("/api/tasks", json={"title": "공유 업무", "assignee_id": me["id"]}).get_json()

    client.post("/api/logout")
    other = _register_and_login(client, "T007")

    client.post("/api/logout")
    _register_and_login(client, "T006")
    share_resp = client.post(f"/api/tasks/{created['id']}/shares", json={"user_id": other["id"]})
    assert share_resp.status_code == 201

    client.post("/api/logout")
    _register_and_login(client, "T007")
    resp = client.get(f"/api/tasks/{created['id']}")
    assert resp.status_code == 200


def test_update_status_to_completed(client):
    me = _register_and_login(client, "T008")
    created = client.post("/api/tasks", json={"title": "완료할 업무", "assignee_id": me["id"]}).get_json()

    resp = client.patch(f"/api/tasks/{created['id']}/status", json={"status": "완료"})
    assert resp.status_code == 200
    assert resp.get_json()["status"] == "완료"


def test_update_status_with_invalid_value_fails(client):
    me = _register_and_login(client, "T009")
    created = client.post("/api/tasks", json={"title": "업무", "assignee_id": me["id"]}).get_json()

    resp = client.patch(f"/api/tasks/{created['id']}/status", json={"status": "보류"})
    assert resp.status_code == 400


def test_list_tasks_only_shows_visible_tasks(client):
    me = _register_and_login(client, "T010")
    client.post("/api/tasks", json={"title": "내 업무", "assignee_id": me["id"]})

    client.post("/api/logout")
    _register_and_login(client, "T011")
    resp = client.get("/api/tasks")
    titles = [t["title"] for t in resp.get_json()]
    assert "내 업무" not in titles


def test_delete_task_with_share_succeeds_without_500(client):
    me = _register_and_login(client, "T012")
    created = client.post("/api/tasks", json={"title": "공유된 업무", "assignee_id": me["id"]}).get_json()

    client.post("/api/logout")
    other = _register_and_login(client, "T013")

    client.post("/api/logout")
    _register_and_login(client, "T012", password="pw1234")
    share_resp = client.post(f"/api/tasks/{created['id']}/shares", json={"user_id": other["id"]})
    assert share_resp.status_code == 201

    resp = client.delete(f"/api/tasks/{created['id']}")
    assert resp.status_code == 200
    assert "error" not in (resp.get_json() or {})
