import requests, json

pdf_path = r"C:\Claude(바이브코딩)\Study\IT_Error\Data\이영동 선생님 전산장애대응.pdf"

with open(pdf_path, "rb") as f:
    r = requests.post(
        "http://127.0.0.1:8000/api/parse-pdf",
        files={"file": ("test.pdf", f, "application/pdf")}
    )

d = r.json()
pages = d.get("pages", [])
print(f"페이지 수: {len(pages)}")
for p in pages:
    print(f"\n=== {p['page']}페이지 ({len(p['lines'])}줄) ===")
    for line in p["lines"][:8]:
        print(" ", line)
