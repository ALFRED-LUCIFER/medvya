import type { Facility, InvitationCreated, InvitationPreview, InviteInput } from '../domain/invitation';

/** A failed API call; status lets the controller tell "no access" (401/403) from other failures. */
export class HttpError extends Error {
  readonly status: number;
  constructor(status: number, what: string) { super(`${what} ${status}`); this.status = status; }
}

const json = { 'Content-Type': 'application/json', 'X-Medvya-Csrf': '1' };

/** The pending invitation, or null when the link is unknown, expired, revoked or already used. POST keeps the token out of URLs. */
export async function previewInvitation(token: string): Promise<InvitationPreview | null> {
  const res = await fetch('/v1/invitations/preview', { method: 'POST', credentials: 'same-origin', headers: json, body: JSON.stringify({ token }) });
  if (res.status === 404) return null;
  if (!res.ok) throw new HttpError(res.status, 'invitation preview');
  return res.json() as Promise<InvitationPreview>;
}

/** Join: sign in with MFA; the server accepts the invitation during sign-in and returns to the app. */
export function joinWithInvitation(token: string) {
  window.location.assign(`/bff/login?returnUrl=%2F&invitation=${encodeURIComponent(token)}`);
}

export async function fetchFacilities(): Promise<Facility[]> {
  const res = await fetch('/v1/invitations/facilities', { credentials: 'same-origin' });
  if (!res.ok) throw new HttpError(res.status, 'facilities');
  return res.json() as Promise<Facility[]>;
}

export async function createInvitation(input: InviteInput): Promise<InvitationCreated> {
  const res = await fetch('/v1/invitations', { method: 'POST', credentials: 'same-origin', headers: json, body: JSON.stringify(input) });
  if (!res.ok) throw new HttpError(res.status, 'invite');
  return res.json() as Promise<InvitationCreated>;
}
