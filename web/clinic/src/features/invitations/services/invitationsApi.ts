import { apiFetch } from '../../api/services/apiClient';
import type { InvitationPreview } from '../domain/invitation';

/** A failed API call; status lets the controller tell "no access" (401/403) from other failures. */
export class HttpError extends Error {
  readonly status: number;
  constructor(status: number, what: string) { super(`${what} ${status}`); this.status = status; }
}

const json = { 'Content-Type': 'application/json' };

/** The pending invitation, or null when the link is unknown, expired, revoked or already used. POST keeps the token out of URLs. */
export async function previewInvitation(token: string): Promise<InvitationPreview | null> {
  const res = await apiFetch('/v1/invitations/preview', { method: 'POST', credentials: 'same-origin', headers: json, body: JSON.stringify({ token }) });
  if (res.status === 404) return null;
  if (!res.ok) throw new HttpError(res.status, 'invitation preview');
  return res.json() as Promise<InvitationPreview>;
}

/**
 * Join: sign in with MFA; the server accepts the invitation during sign-in and returns to the app. The token goes in the POST body and the
 * page then follows the provider address itself, so it never sits in a Medvya URL. A refused start shows "sign-in did not complete".
 */
export async function joinWithInvitation(token: string) {
  try {
    const res = await apiFetch('/bff/login', { method: 'POST', credentials: 'same-origin', headers: json, body: JSON.stringify({ returnUrl: import.meta.env.BASE_URL, invitation: token }) });
    if (res.ok) { window.location.assign(((await res.json()) as { location: string }).location); return; }
  } catch { /* offline or timed out: same message as a refusal */ }
  window.location.assign(`${import.meta.env.BASE_URL}?signin=failed`);
}
