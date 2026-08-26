def test_unknown_path_returns_json_404(client):
    resp = client.get("/api/nonexistent")
    assert resp.status_code == 404
    assert resp.content_type.startswith("application/json")
    data = resp.get_json()
    assert data is not None
    assert "error" in data
