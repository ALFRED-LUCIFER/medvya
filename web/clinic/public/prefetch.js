// MDV-P1-037: starts the session read that every staff screen waits for while the app's bundle is still downloading, instead of
// after it; fetchMeContext takes the answer (sessionApi.ts). A plain script, so it runs before the bundle has arrived. Passive like
// every context read. Failures resolve to undefined and the app reads again. The waiting-room TV never reads a staff session.
if (!location.pathname.includes('/queue/tv')) {
  const context = fetch('/v1/me/context', { credentials: 'same-origin', headers: { 'X-Medvya-Passive': '1' }, signal: AbortSignal.timeout(15000) })
    .catch(() => undefined);
  window.medvyaMeContext = context;
  // MDV-P1-110: on the schedule, the active facility's day is read as soon as the session answers, not after the bundle. Ids and the
  // address's date only, never patient data. fetchScheduleDay takes it only when its URL is the one it would ask for (scheduleApi.ts).
  if (location.pathname.endsWith('/schedule')) {
    // The same real-day check as readAddress (features/schedule/domain/views.ts): a broken date is left out, as the app leaves it out.
    const asked = new URLSearchParams(location.search).get('date');
    const date = asked && /^\d{4}-\d{2}-\d{2}$/.test(asked) && new Date(`${asked}T12:00:00Z`).toISOString().startsWith(asked) ? asked : null;
    window.medvyaSchedule = context
      .then(res => (res && res.ok ? res.clone().json() : undefined))
      .then(me => {
        const facilityId = me && me.active && me.active.facilityId;
        if (!facilityId) return undefined;
        const url = `/v1/schedule?${new URLSearchParams(date ? { facilityId, date } : { facilityId })}`;
        return fetch(url, { credentials: 'same-origin', signal: AbortSignal.timeout(15000) }).then(res => ({ url, res }));
      })
      .catch(() => undefined);
  }
}
