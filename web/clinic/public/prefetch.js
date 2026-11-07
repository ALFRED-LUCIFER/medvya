// MDV-P1-037: starts the session read that every staff screen waits for while the app's bundle is still downloading, instead of
// after it; fetchMeContext takes the answer (sessionApi.ts). A plain script, so it runs before the bundle has arrived. Passive like
// every context read. Failures resolve to undefined and the app reads again. The waiting-room TV never reads a staff session.
if (!location.pathname.includes('/queue/tv')) {
  window.medvyaMeContext = fetch('/v1/me/context', { credentials: 'same-origin', headers: { 'X-Medvya-Passive': '1' }, signal: AbortSignal.timeout(15000) })
    .catch(() => undefined);
}
