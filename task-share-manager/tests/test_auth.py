def register(client, emp_no="E100", name="테스트", dept="IT", password="pw1234"):
    return client.post(
        "/api/register",
        json={"emp_no": emp_no, "name": name, "dept": dept, "password": password},
    )


def test_register_then_login_succeeds(client):
    resp = register(client)
    assert resp.status_code == 201

    resp = client.post("/api/login", json={"emp_no": "E100", "password": "pw1234"})
    assert resp.status_code == 200
    assert resp.get_json()["emp_no"] == "E100"


def test_login_with_wrong_password_fails(client):
    register(client)
    resp = client.post("/api/login", json={"emp_no": "E100", "password": "wrong"})
    assert resp.status_code == 401
    assert "error" in resp.get_json()


def test_duplicate_emp_no_register_fails(client):
    register(client)
    resp = register(client)
    assert resp.status_code == 409


def test_me_requires_login(client):
    resp = client.get("/api/me")
    assert resp.status_code == 401


def test_me_returns_current_user_after_login(client):
    register(client)
    client.post("/api/login", json={"emp_no": "E100", "password": "pw1234"})
    resp = client.get("/api/me")
    assert resp.status_code == 200
    assert resp.get_json()["emp_no"] == "E100"


def test_logout_clears_session(client):
    register(client)
    client.post("/api/login", json={"emp_no": "E100", "password": "pw1234"})
    client.post("/api/logout")
    resp = client.get("/api/me")
    assert resp.status_code == 401
