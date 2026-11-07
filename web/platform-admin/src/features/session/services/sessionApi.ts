import { getJson, post, ApiError } from '../../api/services/api';
import type { Me } from '../domain/session';

/** The signed-on member, or null when there is no session (401). */
export async function fetchMe(): Promise<Me | null> {
  try { return await getJson<Me>('/v1/me'); }
  catch (e) { if (e instanceof ApiError && e.status === 401) return null; throw e; }
}

/** Company sign-on. A key link rides to the server once, in the protected sign-in state; the server then keeps it in the session. */
export function startSignOn(link: string | null) {
  const to = link ? `/bff/login?returnUrl=%2F&link=${encodeURIComponent(link)}` : '/bff/login?returnUrl=%2F';
  window.location.assign(to);
}

export const currentFragment = () => window.location.hash;

/** Asks the browser for the member's security key (WebAuthn get) and sends the signed answer. */
export async function touchKey(): Promise<void> {
  const options = (await (await post('/bff/key/check/options')).json()) as PublicKeyCredentialRequestOptionsJSON;
  const credential = await navigator.credentials.get({ publicKey: PublicKeyCredential.parseRequestOptionsFromJSON(options) });
  await post('/bff/key/check', (credential as PublicKeyCredential).toJSON());
}

/** Registers a new security key (WebAuthn create) with the link this session signed on with. */
export async function registerKey(): Promise<void> {
  const options = (await (await post('/bff/key/registration/options')).json()) as PublicKeyCredentialCreationOptionsJSON;
  const credential = await navigator.credentials.create({ publicKey: PublicKeyCredential.parseCreationOptionsFromJSON(options) });
  await post('/bff/key/registration', (credential as PublicKeyCredential).toJSON());
  window.history.replaceState(null, '', '/'); // the link is spent; drop it from the address bar
}

export async function signOut(): Promise<void> {
  await post('/bff/logout');
  window.location.assign('/');
}

/** True when the server sent the browser back with ?signin=failed (the reason stays in the server log). */
export const signOnFailed = () => new URLSearchParams(window.location.search).get('signin') === 'failed';
