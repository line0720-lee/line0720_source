async function requireLogin() {
  const resp = await fetch("/api/me");
  if (!resp.ok) {
    window.location.href = "/login";
    return null;
  }
  const user = await resp.json();
  const nameEl = document.getElementById("current-user-name");
  if (nameEl) nameEl.textContent = `${user.name} (${user.dept || "-"})`;
  return user;
}

document.addEventListener("DOMContentLoaded", () => {
  const logoutBtn = document.getElementById("logout-btn");
  if (logoutBtn) {
    logoutBtn.addEventListener("click", async () => {
      await fetch("/api/logout", { method: "POST" });
      window.location.href = "/login";
    });
  }
});
