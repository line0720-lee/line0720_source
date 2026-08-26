let taskModal;

function escapeHtml(str) {
  if (!str) return str;
  const map = {
    '&': '&amp;',
    '<': '&lt;',
    '>': '&gt;',
    '"': '&quot;',
    "'": '&#39;'
  };
  return str.replace(/[&<>"']/g, m => map[m]);
}

async function loadCategories() {
  const resp = await fetch("/api/categories");
  const categories = await resp.json();
  const filterSelect = document.getElementById("category-filter");
  const formSelect = document.getElementById("task-category");

  // Clear existing options, preserving placeholder in category-filter
  // category-filter has placeholder at index 0, remove options at index 1+
  while (filterSelect.options.length > 1) {
    filterSelect.remove(1);
  }
  // task-category has no placeholder, clear completely
  formSelect.innerHTML = "";

  categories.forEach((c) => {
    const opt1 = new Option(c.name, c.id);
    filterSelect.add(opt1);
    const opt2 = new Option(c.name, c.id);
    formSelect.add(opt2);
  });
  return categories;
}

function statusBadgeClass(status) {
  return { "진행중": "primary", "완료": "success", "취소": "secondary" }[status] || "secondary";
}

function renderTasks(tasks) {
  const list = document.getElementById("task-list");
  list.innerHTML = "";
  tasks.forEach((t) => {
    const col = document.createElement("div");
    col.className = "col-md-4";
    col.innerHTML = `
      <div class="card status-${t.status}">
        <div class="card-body">
          <h5 class="card-title">${escapeHtml(t.title)}</h5>
          <span class="badge bg-${statusBadgeClass(t.status)}">${t.status}</span>
          <p class="card-text mt-2">${escapeHtml(t.content || "")}</p>
          <p class="text-muted small">${escapeHtml(t.start_date || "-")} ~ ${escapeHtml(t.end_date || "-")}</p>
          <div class="d-flex gap-2">
            <button class="btn btn-sm btn-outline-primary edit-btn" data-id="${t.id}">수정</button>
            <button class="btn btn-sm btn-outline-success complete-btn" data-id="${t.id}">완료 처리</button>
            <button class="btn btn-sm btn-outline-danger cancel-btn" data-id="${t.id}">취소</button>
          </div>
        </div>
      </div>`;
    list.appendChild(col);
  });

  list.querySelectorAll(".edit-btn").forEach((btn) =>
    btn.addEventListener("click", () => openTaskModal(btn.dataset.id))
  );
  list.querySelectorAll(".complete-btn").forEach((btn) =>
    btn.addEventListener("click", () => changeStatus(btn.dataset.id, "완료"))
  );
  list.querySelectorAll(".cancel-btn").forEach((btn) =>
    btn.addEventListener("click", () => changeStatus(btn.dataset.id, "취소"))
  );
}

async function loadTasks() {
  const category = document.getElementById("category-filter").value;
  const status = document.getElementById("status-filter").value;
  const params = new URLSearchParams();
  if (category) params.set("category_id", category);
  if (status) params.set("status", status);

  const resp = await fetch(`/api/tasks?${params.toString()}`);
  const tasks = await resp.json();
  renderTasks(tasks);
}

async function changeStatus(taskId, status) {
  await fetch(`/api/tasks/${taskId}/status`, {
    method: "PATCH",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ status }),
  });
  loadTasks();
  loadStatusSummary();
}

async function openTaskModal(taskId) {
  document.getElementById("task-form-error").classList.add("d-none");
  document.getElementById("task-form").reset();
  document.getElementById("task-id").value = taskId || "";

  if (taskId) {
    const resp = await fetch(`/api/tasks/${taskId}`);
    const t = await resp.json();
    document.getElementById("task-title").value = t.title;
    document.getElementById("task-content").value = t.content || "";
    document.getElementById("task-category").value = t.category_id || "";
    document.getElementById("task-assignee").value = t.assignee_id;
    document.getElementById("task-start").value = t.start_date || "";
    document.getElementById("task-end").value = t.end_date || "";
    document.getElementById("task-status").value = t.status;
  }
  taskModal.show();
}

async function submitTaskForm(e) {
  e.preventDefault();
  const taskId = document.getElementById("task-id").value;
  const payload = {
    title: document.getElementById("task-title").value,
    content: document.getElementById("task-content").value,
    category_id: document.getElementById("task-category").value || null,
    assignee_id: Number(document.getElementById("task-assignee").value),
    start_date: document.getElementById("task-start").value || null,
    end_date: document.getElementById("task-end").value || null,
    status: document.getElementById("task-status").value,
  };

  const url = taskId ? `/api/tasks/${taskId}` : "/api/tasks";
  const method = taskId ? "PUT" : "POST";
  const resp = await fetch(url, {
    method,
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });

  if (resp.ok) {
    taskModal.hide();
    loadTasks();
    loadStatusSummary();
  } else {
    const data = await resp.json();
    const errBox = document.getElementById("task-form-error");
    errBox.textContent = data.error || "저장에 실패했습니다";
    errBox.classList.remove("d-none");
  }
}

let categoryModal;

function renderCategoryManageList(categories) {
  const list = document.getElementById("category-manage-list");
  const shareSelect = document.getElementById("share-category-select");
  list.innerHTML = "";
  shareSelect.innerHTML = "";
  categories.forEach((c) => {
    const li = document.createElement("li");
    li.className = "list-group-item";
    li.textContent = `${c.name} — ${c.description || ""}`;
    list.appendChild(li);
    shareSelect.add(new Option(c.name, c.id));
  });
}

document.getElementById("new-category-btn")?.addEventListener("click", async () => {
  if (!categoryModal) categoryModal = new bootstrap.Modal(document.getElementById("category-modal"));
  const resp = await fetch("/api/categories");
  renderCategoryManageList(await resp.json());
  categoryModal.show();
});

document.getElementById("new-category-form")?.addEventListener("submit", async (e) => {
  e.preventDefault();
  const name = document.getElementById("new-category-name").value;
  await fetch("/api/categories", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ name }),
  });
  document.getElementById("new-category-name").value = "";
  const resp = await fetch("/api/categories");
  const categories = await resp.json();
  renderCategoryManageList(categories);
  await loadCategories();
});

document.getElementById("add-share-btn")?.addEventListener("click", async () => {
  const categoryId = document.getElementById("share-category-select").value;
  const userId = Number(document.getElementById("share-user-id").value);
  if (!categoryId || !userId) return;
  const resp = await fetch(`/api/categories/${categoryId}/shares`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ user_id: userId }),
  });
  if (resp.ok) {
    document.getElementById("share-user-id").value = "";
    alert("공유 대상자가 등록되었습니다");
  } else {
    const data = await resp.json();
    alert(data.error || "등록 실패");
  }
});

document.addEventListener("DOMContentLoaded", async () => {
  const user = await requireLogin();
  if (!user) return;

  taskModal = new bootstrap.Modal(document.getElementById("task-modal"));
  await loadCategories();
  await loadTasks();
  await loadTodayWidget();
  await loadStatusSummary();

  const params = new URLSearchParams(window.location.search);
  if (params.get("edit")) {
    openTaskModal(params.get("edit"));
  } else if (params.get("new_date")) {
    await openTaskModal(null);
    document.getElementById("task-start").value = params.get("new_date");
    document.getElementById("task-end").value = params.get("new_date");
  }

  document.getElementById("category-filter").addEventListener("change", loadTasks);
  document.getElementById("status-filter").addEventListener("change", loadTasks);
  document.getElementById("new-task-btn").addEventListener("click", () => openTaskModal(null));
  document.getElementById("task-form").addEventListener("submit", submitTaskForm);
});

function todayStr() {
  const d = new Date();
  const year = d.getFullYear();
  const month = String(d.getMonth() + 1).padStart(2, "0");
  const day = String(d.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

async function loadTodayWidget() {
  const resp = await fetch("/api/tasks");
  const tasks = await resp.json();
  const today = todayStr();

  const todays = tasks.filter(
    (t) => t.status !== "취소" && (t.start_date === today || t.end_date === today)
  );

  const priorityCategoryNames = ["수술", "시술", "치료"];
  todays.sort((a, b) => {
    const aPriority = priorityCategoryNames.includes(a.category_name) ? 0 : 1;
    const bPriority = priorityCategoryNames.includes(b.category_name) ? 0 : 1;
    return aPriority - bPriority;
  });

  const list = document.getElementById("today-list");
  list.innerHTML = "";
  if (todays.length === 0) {
    list.innerHTML = '<li class="list-group-item text-muted">오늘 시작/마감 업무가 없습니다</li>';
    return;
  }
  todays.forEach((t) => {
    const li = document.createElement("li");
    li.className = "list-group-item";
    li.textContent = `[${t.status}] ${t.title} (${t.start_date} ~ ${t.end_date || "-"})`;
    list.appendChild(li);
  });
}

async function loadStatusSummary() {
  const resp = await fetch("/api/tasks");
  const tasks = await resp.json();

  const counts = { "진행중": 0, "완료": 0, "취소": 0 };
  tasks.forEach((t) => { counts[t.status] = (counts[t.status] || 0) + 1; });
  const total = tasks.length;
  const progress = total > 0 ? Math.round((counts["완료"] / total) * 100) : 0;

  const el = document.getElementById("status-summary");
  el.innerHTML = `
    <div class="col-auto"><span class="badge bg-primary">진행중 ${counts["진행중"]}</span></div>
    <div class="col-auto"><span class="badge bg-success">완료 ${counts["완료"]}</span></div>
    <div class="col-auto"><span class="badge bg-secondary">취소 ${counts["취소"]}</span></div>
    <div class="col-auto"><span class="badge bg-info text-dark">진행률 ${progress}%</span></div>
  `;
}
