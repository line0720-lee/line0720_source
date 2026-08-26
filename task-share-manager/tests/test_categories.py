def _register_and_login(client, emp_no, name="사용자", password="pw1234"):
    client.post("/api/register", json={"emp_no": emp_no, "name": name, "dept": "IT", "password": password})
    client.post("/api/login", json={"emp_no": emp_no, "password": password})


def test_create_and_list_category(client):
    _register_and_login(client, "E200")
    resp = client.post("/api/categories", json={"name": "테스트카테고리", "description": "설명"})
    assert resp.status_code == 201

    resp = client.get("/api/categories")
    names = [c["name"] for c in resp.get_json()]
    assert "테스트카테고리" in names


def test_non_owner_cannot_view_unshared_category(client):
    _register_and_login(client, "E201")
    created = client.post("/api/categories", json={"name": "비공개", "description": ""}).get_json()

    client.post("/api/logout")
    _register_and_login(client, "E202")
    resp = client.get(f"/api/categories/{created['id']}")
    assert resp.status_code == 403


def test_shared_user_can_view_category(client):
    _register_and_login(client, "E203")
    created = client.post("/api/categories", json={"name": "공유대상", "description": ""}).get_json()

    client.post("/api/logout")
    _register_and_login(client, "E204")
    me = client.get("/api/me").get_json()

    client.post("/api/logout")
    _register_and_login(client, "E203", password="pw1234")
    share_resp = client.post(f"/api/categories/{created['id']}/shares", json={"user_id": me["id"]})
    assert share_resp.status_code == 201

    client.post("/api/logout")
    _register_and_login(client, "E204", password="pw1234")
    resp = client.get(f"/api/categories/{created['id']}")
    assert resp.status_code == 200


def test_only_owner_can_delete_category(client):
    _register_and_login(client, "E205")
    created = client.post("/api/categories", json={"name": "삭제대상", "description": ""}).get_json()

    client.post("/api/logout")
    _register_and_login(client, "E206")
    resp = client.delete(f"/api/categories/{created['id']}")
    assert resp.status_code == 403
