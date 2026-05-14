let rowCount = 0;

function fmt(n) {
  return Number(n || 0).toLocaleString('ko-KR');
}

function recalc() {
  let supply = 0;
  document.querySelectorAll('#lineBody tr.line-row').forEach(row => {
    const qty   = parseInt(row.querySelector('.qty').value)   || 0;
    const price = parseInt(row.querySelector('.price').value) || 0;
    const amt   = qty * price;
    row.querySelector('.amount').value = amt;
    row.querySelector('.amount-display').textContent = fmt(amt) + '원';
    supply += amt;
  });
  const vat   = Math.round(supply * 0.1);
  const grand = supply + vat;
  document.getElementById('supplyTotal').textContent = fmt(supply) + '원';
  document.getElementById('vatTotal').textContent    = fmt(vat)    + '원';
  document.getElementById('grandTotal').textContent  = fmt(grand)  + '원';
}

function addRow(name = '', size = '', qty = 1, price = 0, remarks = '') {
  const hint = document.getElementById('emptyHint');
  if (hint) hint.remove();

  rowCount++;
  const tr = document.createElement('tr');
  tr.className = 'line-row';
  tr.innerHTML = `
    <td class="ps-3 text-muted small seq">${rowCount}</td>
    <td><input type="text"   class="form-control form-control-sm name-input" name="item_name[]"    value="${escHtml(name)}"    placeholder="품명"></td>
    <td><input type="text"   class="form-control form-control-sm"             name="item_size[]"    value="${escHtml(size)}"    placeholder="사이즈"></td>
    <td><input type="number" class="form-control form-control-sm qty"          name="item_qty[]"     value="${qty}" min="1"></td>
    <td><input type="number" class="form-control form-control-sm price"        name="item_price[]"   value="${price}" min="0"></td>
    <td>
      <input type="hidden" class="amount" name="item_amount[]" value="0">
      <span class="amount-display fw-semibold text-end d-block">${fmt(qty * price)}원</span>
    </td>
    <td><input type="text"   class="form-control form-control-sm"             name="item_remarks[]" value="${escHtml(remarks)}" placeholder="비고"></td>
    <td class="text-center">
      <button type="button" class="btn btn-sm btn-outline-danger" onclick="removeRow(this)">
        <i class="bi bi-x-lg"></i>
      </button>
    </td>
  `;

  tr.querySelector('.qty').addEventListener('input', recalc);
  tr.querySelector('.price').addEventListener('input', recalc);

  document.getElementById('lineBody').appendChild(tr);
  recalc();
  renumberRows();
}

function addCustomRow() {
  addRow();
}

function removeRow(btn) {
  btn.closest('tr').remove();
  renumberRows();
  recalc();
  if (!document.querySelectorAll('#lineBody tr.line-row').length) {
    const tbody = document.getElementById('lineBody');
    tbody.innerHTML = `
      <tr id="emptyHint">
        <td colspan="8" class="text-center text-muted py-5">
          <i class="bi bi-arrow-up-circle fs-4 d-block mb-2"></i>위 버튼으로 품목을 추가하세요.
        </td>
      </tr>`;
  }
}

function renumberRows() {
  document.querySelectorAll('#lineBody tr.line-row').forEach((row, i) => {
    row.querySelector('.seq').textContent = i + 1;
  });
  rowCount = document.querySelectorAll('#lineBody tr.line-row').length;
}

function addSelected() {
  const checked = document.querySelectorAll('.pick-chk:checked');
  if (!checked.length) { alert('품목을 선택해주세요.'); return; }
  checked.forEach(chk => {
    const row = chk.closest('tr');
    addRow(
      row.dataset.name,
      row.dataset.size,
      1,
      parseInt(row.dataset.price) || 0,
      row.dataset.remarks
    );
    chk.checked = false;
  });
  bootstrap.Modal.getInstance(document.getElementById('pickModal')).hide();
}

// 품목 검색 필터
document.addEventListener('DOMContentLoaded', () => {
  const searchInput = document.getElementById('pickSearch');
  if (searchInput) {
    searchInput.addEventListener('input', function () {
      const q = this.value.toLowerCase();
      document.querySelectorAll('#pickBody .pick-row').forEach(row => {
        row.style.display = row.dataset.name.toLowerCase().includes(q) ? '' : 'none';
      });
    });
  }

  // 행 클릭으로 체크박스 토글 (이벤트 위임 — 동적 추가 행에도 동작)
  const pickBody = document.getElementById('pickBody');
  if (pickBody) {
    pickBody.addEventListener('click', e => {
      const row = e.target.closest('.pick-row');
      if (row && e.target.type !== 'checkbox') {
        const chk = row.querySelector('.pick-chk');
        chk.checked = !chk.checked;
      }
    });
  }

  // 폼 제출 전 검증
  const form = document.getElementById('quoteForm');
  if (form) {
    form.addEventListener('submit', e => {
      if (!document.querySelectorAll('#lineBody tr.line-row').length) {
        e.preventDefault();
        alert('품목을 1개 이상 추가해주세요.');
      }
    });
  }
});

function escHtml(str) {
  return String(str || '')
    .replace(/&/g, '&amp;')
    .replace(/"/g, '&quot;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;');
}
