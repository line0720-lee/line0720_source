function exclusiveEnd(dateStr) {
  if (!dateStr) return dateStr;
  const d = new Date(dateStr + "T00:00:00");
  d.setDate(d.getDate() + 1);
  const year = d.getFullYear();
  const month = String(d.getMonth() + 1).padStart(2, "0");
  const day = String(d.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

document.addEventListener("DOMContentLoaded", async () => {
  const user = await requireLogin();
  if (!user) return;

  const resp = await fetch("/api/tasks");
  const tasks = await resp.json();

  const events = tasks.map((t) => ({
    id: t.id,
    title: t.title,
    start: t.start_date,
    end: exclusiveEnd(t.end_date),
    classNames: [`status-${t.status}`],
  }));

  const calendarEl = document.getElementById("calendar");
  const calendar = new FullCalendar.Calendar(calendarEl, {
    initialView: "dayGridMonth",
    locale: "ko",
    height: 800,
    headerToolbar: {
      left: "prev,next today",
      center: "title",
      right: "dayGridMonth,timeGridWeek",
    },
    events,
    eventClick: (info) => {
      window.location.href = `/dashboard?edit=${info.event.id}`;
    },
    dateClick: (info) => {
      window.location.href = `/dashboard?new_date=${info.dateStr}`;
    },
  });
  calendar.render();
});
