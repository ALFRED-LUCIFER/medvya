import type { MeContext } from '../domain/session';

// The server holds the tokens (BFF, ADR-004); the browser only sends its HTTP-only session cookie.
const csrf = { 'X-Medvya-Csrf': '1' };

/** The server answered 401: the session ended (idle timeout, revoked, signed out elsewhere). */
export class SessionEndedError extends Error {}

/**
 * The signed-in user and working context, or null when there is no session. Marked passive: the status poll must not
 * count as activity, or an unattended screen would never reach its idle timeout.
 */
export async function fetchMeContext(): Promise<MeContext | null> {
  const res = await fetch('/v1/me/context', { credentials: 'same-origin', headers: { 'X-Medvya-Passive': '1' } });
  if (res.status === 401) return null;
  if (!res.ok) throw new Error(`me/context ${res.status}`);
  return res.json() as Promise<MeContext>;
}

/** Scopes the session to one of the user's memberships (facility picker, MDV-P0-012). */
export async function chooseFacility(membershipId: string) {
  const res = await fetch('/v1/me/facility', {
    method: 'POST', credentials: 'same-origin', headers: { ...csrf, 'Content-Type': 'application/json' }, body: JSON.stringify({ membershipId }),
  });
  if (res.status === 401) throw new SessionEndedError();
  if (!res.ok) throw new Error(`me/facility ${res.status}`);
}

/** Starts sign-in and comes back to the page the user was on (F-05a: return to the previous route). */
export function startSignIn() {
  window.location.assign(`/bff/login?returnUrl=${encodeURIComponent(window.location.pathname)}`);
}

export async function signOut() {
  await fetch('/bff/logout', { method: 'POST', credentials: 'same-origin', headers: csrf });
}

// Per tab and PHI-free: only "this tab had a session", so a reload after an idle timeout still shows F-05a.
const marker = 'medvya.hadSession';

export function hadSession() {
  try { return sessionStorage.getItem(marker) === '1'; } catch { return false; }
}

export function rememberSession(had: boolean) {
  try { if (had) sessionStorage.setItem(marker, '1'); else sessionStorage.removeItem(marker); } catch { /* storage blocked: F-05a then shows only within the page's lifetime */ }
}

/** True when the server sent the user back with ?signin=failed (reason stays server-side). */
export const signInFailed = () => new URLSearchParams(window.location.search).get('signin') === 'failed';
