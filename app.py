import io
import os
from datetime import date, timedelta
from flask import (
    Flask, render_template, request, redirect,
    url_for, jsonify, send_file, flash
)
from database import (
    init_db,
    get_all_vendors, get_vendor,
    get_all_settings, save_settings, get_setting,
    get_all_items, get_item, create_item, update_item, delete_item,
    get_items_count_by_vendor,
    create_quotation, update_quotation, update_quotation_pdf,
    get_quotation, get_all_quotations, get_quotation_stats, delete_quotation,
    update_quotation_status,
)
from pdf_generator import generate_pdf, generate_pdf_bytes

app = Flask(__name__)
app.secret_key = os.environ.get("SECRET_KEY", "chassis-quote-secret-2026")


@app.before_request
def setup():
    init_db()


# ── 홈 / 대시보드 ──────────────────────────────────────────────────────────

@app.route("/")
def index():
    vendor_id = request.args.get("vendor_id", type=int)
    vendors = get_all_vendors()
    stats = get_quotation_stats()
    items_count = get_items_count_by_vendor()

    if vendor_id:
        active_vendor = get_vendor(vendor_id)
        quotes = get_all_quotations(vendor_id=vendor_id)
        items = get_all_items(vendor_id=vendor_id)
    else:
        active_vendor = None
        quotes = get_all_quotations()
        items = get_all_items()

    today = date.today().isoformat()
    this_month = date.today().strftime("%Y-%m")

    # 전체 이번달 합계
    total_month = sum(
        (q["grand_total"] or 0)
        for q in quotes
        if (q["created_at"] or "")[:7] == this_month
    )

    return render_template(
        "index.html",
        vendors=vendors,
        active_vendor=active_vendor,
        quotes=quotes,
        items=items,
        stats=stats,
        items_count=items_count,
        today=today,
        this_month=this_month,
        total_month=total_month,
    )


# ── 설정 ──────────────────────────────────────────────────────────────────

@app.route("/settings", methods=["GET", "POST"])
def settings():
    if request.method == "POST":
        data = {
            "company_name": request.form.get("company_name", "샤시크루"),
            "address": request.form.get("address", ""),
            "phone": request.form.get("phone", ""),
            "fax": request.form.get("fax", ""),
            "manager": request.form.get("manager", ""),
            "bank_account": request.form.get("bank_account", ""),
        }
        save_settings(data)
        flash("설정이 저장되었습니다.", "success")
        return redirect(url_for("settings"))
    cfg = get_all_settings()
    return render_template("settings.html", cfg=cfg)


# ── 품목 관리 ─────────────────────────────────────────────────────────────

@app.route("/items")
def items():
    vendor_id = request.args.get("vendor_id", type=int)
    vendors = get_all_vendors()

    if vendor_id:
        active_vendor = get_vendor(vendor_id)
        all_items = get_all_items(vendor_id=vendor_id)
    else:
        active_vendor = vendors[0] if vendors else None
        all_items = get_all_items(vendor_id=active_vendor["id"] if active_vendor else None)

    return render_template("items.html",
                           vendors=vendors,
                           active_vendor=active_vendor,
                           items=all_items)


@app.route("/items/list")
def items_list():
    vendor_id = request.args.get("vendor_id", type=int)
    all_items = get_all_items(vendor_id=vendor_id)
    return jsonify([dict(i) for i in all_items])


@app.route("/items", methods=["POST"])
def item_create():
    data = request.get_json()
    try:
        iid = create_item(
            data["vendor_id"], data["name"],
            data.get("size", ""), data.get("unit_price", 0), data.get("remarks", "")
        )
        item = get_item(iid)
        return jsonify({"success": True, "item": dict(item)})
    except Exception as e:
        return jsonify({"success": False, "error": str(e)}), 400


@app.route("/items/<int:item_id>", methods=["PUT"])
def item_update(item_id):
    data = request.get_json()
    try:
        update_item(
            item_id, data["vendor_id"], data["name"],
            data.get("size", ""), data.get("unit_price", 0), data.get("remarks", "")
        )
        item = get_item(item_id)
        return jsonify({"success": True, "item": dict(item)})
    except Exception as e:
        return jsonify({"success": False, "error": str(e)}), 400


@app.route("/items/<int:item_id>", methods=["DELETE"])
def item_delete(item_id):
    try:
        delete_item(item_id)
        return jsonify({"success": True})
    except Exception as e:
        return jsonify({"success": False, "error": str(e)}), 400


# ── 견적서 작성 ───────────────────────────────────────────────────────────

@app.route("/new-quote")
def new_quote():
    vendors = get_all_vendors()
    vendor_id = request.args.get("vendor_id", type=int)

    common_vendor = next((v for v in vendors if v["name"] == "공통"), None)
    common_items = get_all_items(vendor_id=common_vendor["id"]) if common_vendor else []

    if vendor_id:
        active_vendor = get_vendor(vendor_id)
        vendor_items = get_all_items(vendor_id=vendor_id)
    else:
        active_vendor = None
        vendor_items = []

    today = date.today().isoformat()
    valid = (date.today() + timedelta(days=30)).isoformat()
    return render_template("new_quote.html",
                           vendors=vendors,
                           active_vendor=active_vendor,
                           vendor_items=vendor_items,
                           common_items=common_items,
                           today=today,
                           valid=valid)


@app.route("/new-quote", methods=["POST"])
def new_quote_submit():
    f = request.form
    vendor_id = f.get("vendor_id", type=int)
    vendor = get_vendor(vendor_id) if vendor_id else None
    vendor_name = vendor["name"] if vendor else "기타"

    line_items = []
    names = request.form.getlist("item_name[]")
    sizes = request.form.getlist("item_size[]")
    qtys = request.form.getlist("item_qty[]")
    prices = request.form.getlist("item_price[]")
    amounts = request.form.getlist("item_amount[]")
    remarks = request.form.getlist("item_remarks[]")

    for i in range(len(names)):
        if not names[i].strip():
            continue
        line_items.append({
            "name": names[i],
            "size": sizes[i] if i < len(sizes) else "",
            "quantity": int(qtys[i]) if i < len(qtys) and qtys[i] else 1,
            "unit_price": int(prices[i]) if i < len(prices) and prices[i] else 0,
            "amount": int(amounts[i]) if i < len(amounts) and amounts[i] else 0,
            "remarks": remarks[i] if i < len(remarks) else "",
        })

    supply_total = sum(it["amount"] for it in line_items)
    vat = round(supply_total * 0.1)
    grand_total = supply_total + vat

    qid, qnum = create_quotation(
        vendor_id=vendor_id,
        vendor_name=vendor_name,
        customer_company=f.get("customer_company", ""),
        customer_name=f.get("customer_name", ""),
        customer_phone=f.get("customer_phone", ""),
        issue_date=f.get("issue_date", ""),
        valid_until=f.get("valid_until", ""),
        notes=f.get("notes", ""),
        supply_total=supply_total,
        vat=vat,
        grand_total=grand_total,
        line_items=line_items,
    )

    flash("견적서가 저장되었습니다.", "success")
    return redirect(url_for("quote_detail", qid=qid))


# ── 견적 이력 ─────────────────────────────────────────────────────────────

@app.route("/quotes")
def quote_list():
    vendor_id = request.args.get("vendor_id", type=int)
    vendors = get_all_vendors()
    active_vendor = get_vendor(vendor_id) if vendor_id else None
    quotes = get_all_quotations(vendor_id=vendor_id)
    return render_template("quote_list.html",
                           quotes=quotes,
                           vendors=vendors,
                           active_vendor=active_vendor)


@app.route("/quotes/<int:qid>")
def quote_detail(qid):
    q, items = get_quotation(qid)
    if not q:
        flash("견적서를 찾을 수 없습니다.", "danger")
        return redirect(url_for("quote_list"))
    company = get_all_settings()
    return render_template("quote_detail.html", q=q, items=items, company=company)


@app.route("/quotes/<int:qid>/edit", methods=["GET", "POST"])
def quote_edit(qid):
    q, items = get_quotation(qid)
    if not q:
        flash("견적서를 찾을 수 없습니다.", "danger")
        return redirect(url_for("quote_list"))

    vendors = get_all_vendors()

    if request.method == "POST":
        f = request.form
        vendor_id = f.get("vendor_id", type=int)
        vendor = get_vendor(vendor_id) if vendor_id else None
        vendor_name = vendor["name"] if vendor else (q["vendor_name"] or "기타")

        line_items = []
        names   = request.form.getlist("item_name[]")
        sizes   = request.form.getlist("item_size[]")
        qtys    = request.form.getlist("item_qty[]")
        prices  = request.form.getlist("item_price[]")
        amounts = request.form.getlist("item_amount[]")
        remarks = request.form.getlist("item_remarks[]")

        for i in range(len(names)):
            if not names[i].strip():
                continue
            line_items.append({
                "name":       names[i],
                "size":       sizes[i] if i < len(sizes) else "",
                "quantity":   int(qtys[i]) if i < len(qtys) and qtys[i] else 1,
                "unit_price": int(prices[i]) if i < len(prices) and prices[i] else 0,
                "amount":     int(amounts[i]) if i < len(amounts) and amounts[i] else 0,
                "remarks":    remarks[i] if i < len(remarks) else "",
            })

        supply_total = sum(it["amount"] for it in line_items)
        vat          = round(supply_total * 0.1)
        grand_total  = supply_total + vat

        update_quotation(
            qid=qid,
            vendor_id=vendor_id,
            vendor_name=vendor_name,
            customer_company=f.get("customer_company", ""),
            customer_name=f.get("customer_name", ""),
            customer_phone=f.get("customer_phone", ""),
            issue_date=f.get("issue_date", ""),
            valid_until=f.get("valid_until", ""),
            notes=f.get("notes", ""),
            supply_total=supply_total,
            vat=vat,
            grand_total=grand_total,
            line_items=line_items,
        )
        flash("견적서가 수정되었습니다.", "success")
        return redirect(url_for("quote_detail", qid=qid))

    # GET — 편집 폼 렌더링
    active_vendor = get_vendor(q["vendor_id"]) if q["vendor_id"] else None
    vendor_items  = get_all_items(vendor_id=q["vendor_id"]) if q["vendor_id"] else []
    common_vendor = next((v for v in vendors if v["name"] == "공통"), None)
    common_items  = get_all_items(vendor_id=common_vendor["id"]) if common_vendor else []
    common_vendor_id = common_vendor["id"] if common_vendor else None
    return render_template(
        "quote_edit.html",
        q=q,
        items=items,
        vendors=vendors,
        active_vendor=active_vendor,
        vendor_items=vendor_items,
        common_items=common_items,
        common_vendor_id=common_vendor_id,
    )


@app.route("/quotes/<int:qid>/pdf")
def quote_pdf(qid):
    q, items = get_quotation(qid)
    if not q:
        return "견적서를 찾을 수 없습니다.", 404

    company = get_all_settings()
    try:
        pdf_bytes = generate_pdf_bytes(
            quotation=dict(q),
            line_items=[dict(it) for it in items],
            company=company,
        )
    except Exception as e:
        return f"PDF 생성 오류: {e}", 500

    vendor = (q["vendor_name"] or "").strip()
    cust   = (q["customer_company"] or "거래처").strip()
    filename = f"{q['quote_number']}_{vendor}_{cust}.pdf"

    return send_file(
        io.BytesIO(pdf_bytes),
        mimetype="application/pdf",
        as_attachment=True,
        download_name=filename,
    )


@app.route("/quotes/<int:qid>/status", methods=["POST"])
def quote_status_update(qid):
    data = request.get_json()
    status = data.get("status", "견적")
    if status not in ("견적", "보류", "계약"):
        return jsonify({"success": False, "error": "invalid status"}), 400
    update_quotation_status(qid, status)
    return jsonify({"success": True})


@app.route("/quotes/<int:qid>/delete", methods=["POST"])
def quote_delete(qid):
    q, _ = get_quotation(qid)
    vendor_id = q["vendor_id"] if q else None
    delete_quotation(qid)
    flash("견적서가 삭제되었습니다.", "success")
    if vendor_id:
        return redirect(url_for("quote_list", vendor_id=vendor_id))
    return redirect(url_for("quote_list"))


if __name__ == "__main__":
    init_db()
    port = int(os.environ.get("PORT", 5000))
    debug = os.environ.get("FLASK_ENV") != "production"
    app.run(debug=debug, host="0.0.0.0", port=port)
