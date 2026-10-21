import type { MeContext } from '../domain/session';

// The server holds the tokens (BFF, ADR-004); the browser only sends its HTTP-only session cookie.
const csrf = { 'X-Medvya-Csrf': '1' };

/** The signed-in user, or null when there is no session. */
export async function fetchMeContext(): Promise<MeContext | null> {
  const res = await fetch('/v1/me/context', { credentials: 'same-origin' });
  if (res.status === 401) return null;
  if (!res.ok) throw new Error(`me/context ${res.status}`);
  return res.json() as Promise<MeContext>;
}

export function startSignIn() {
  window.location.assign(`/bff/login?returnUrl=${encodeURIComponent(window.location.pathname)}`);
}

export async function signOut() {
  await fetch('/bff/logout', { method: 'POST', credentials: 'same-origin', headers: csrf });
}

/** True when the server sent the user back with ?signin=failed (reason stays server-side). */
export const signInFailed = () => new URLSearchParams(window.location.search).get('signin') === 'failed';
