document.addEventListener("DOMContentLoaded", async () => {
  const user = await requireLogin();
  if (!user) return;

  const resp = await fetch("/api/tasks");
  const tasks = await resp.json();

  const events = tasks.map((t) => ({
    id: t.id,
    title: t.title,
    start: t.start_date,
    end: t.end_date,
    classNames: [`status-${t.status}`],
  }));

  const calendarEl = document.getElementById("calendar");
  const calendar = new FullCalendar.Calendar(calendarEl, {
    initialView: "dayGridMonth",
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
