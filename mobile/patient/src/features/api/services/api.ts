import * as SecureStore from 'expo-secure-store';
import { apiBase } from './source';
// shortcut: token handling duplicated from mobile/clinician (api.ts); both move to packages/auth-client when a third app needs it.

/**
 * The patient app's client for Host.Api with native tokens (ADR-011 §2, §3). The refresh token lives in secure-store (this
 * device only), the 15-minute access token in memory only. A 401 refreshes once, one refresh at a time (the server rotates
 * the refresh token, so two with one token would end the session); a refused refresh means the session is over (revoked,
 * replayed, expired) and the app signs out and wipes the phone. Tokens are never logged or put in a URL.
 */
const REFRESH = 'medvya.refresh';
export const deviceOnly = { keychainAccessible: SecureStore.WHEN_UNLOCKED_THIS_DEVICE_ONLY };
export type TokenPair = { access_token: string; refresh_token: string };

let access: string | null = null;
let refreshToken: string | null = null; // the secure-store copy, held so a sign-out can still end the server session after the wipe
let generation = 0; // bumped by forgetTokens: a token answer that arrives after a sign-out is dropped, never stored
let refreshing: Promise<string> | null = null;
let ended = () => {};

/** The server refused the refresh token: the session ended elsewhere (revoked, replayed or expired). */
export class SessionEndedError extends Error {
  constructor() { super('mobile.error.generic'); }
}

/** Who to tell when the server ends the session: the session provider, which signs out and wipes. Returns the unsubscribe. */
export function onSessionEnded(listener: () => void) {
  ended = listener;
  return () => { if (ended === listener) ended = () => {}; };
}

/** POST JSON without a token (the sign-in endpoints). */
export const postAnonymous = (path: string, body: unknown) =>
  fetch(`${apiBase()}${path}`, { method: 'POST', headers: { Accept: 'application/json', 'Content-Type': 'application/json' }, body: JSON.stringify(body) });

/** Keeps the pair `request` returns, unless the phone was signed out meanwhile (then nothing is kept). Null: nothing issued. */
async function grant(request: () => Promise<TokenPair | null>) {
  const since = generation;
  const pair = await request();
  if (!pair) return null;
  if (since !== generation) throw new SessionEndedError();
  await SecureStore.setItemAsync(REFRESH, pair.refresh_token, deviceOnly);
  if (since !== generation) { await SecureStore.deleteItemAsync(REFRESH); throw new SessionEndedError(); } // a wipe ran during the write
  refreshToken = pair.refresh_token;
  access = pair.access_token;
  return access;
}

/** The first token pair, from the sign-in (`request` answers null for a wrong code). */
export const signIn = (request: () => Promise<TokenPair | null>) => grant(request);

/** Reads the stored refresh token into memory (at launch); true when there is one. */
export async function loadTokens() {
  refreshToken = await SecureStore.getItemAsync(REFRESH);
  return refreshToken !== null;
}

/** POST /v1/patient-sign-in/refresh. 400 is invalid_grant: the session ended and will not come back. */
async function postRefresh(token: string) {
  const res = await postAnonymous('/v1/patient-sign-in/refresh', { refreshToken: token });
  if (res.status === 400) throw new SessionEndedError();
  if (!res.ok) throw new Error('mobile.error.generic');
  return (await res.json()) as TokenPair;
}

function refresh() {
  return (refreshing ??= (async () => {
    const token = refreshToken ?? await SecureStore.getItemAsync(REFRESH);
    if (!token) throw new SessionEndedError();
    return (await grant(() => postRefresh(token)))!;
  })().finally(() => { refreshing = null; }));
}

const call = (path: string, init: RequestInit, token: string) =>
  fetch(`${apiBase()}${path}`, { ...init, headers: { Accept: 'application/json', ...init.headers, Authorization: `Bearer ${token}` } });

/** A call as the signed-in patient. A refused refresh signs the phone out (ADR-011 §3: "a revoke, the first 401 from a refresh"). */
export async function send<T>(path: string, init: RequestInit = {}): Promise<T> {
  try {
    let res = await call(path, init, access ?? await refresh());
    if (res.status === 401) res = await call(path, init, await refresh());
    if (!res.ok) throw new Error('mobile.error.generic');
    return (res.status === 204 ? undefined : await res.json()) as T;
  } catch (e) {
    if (e instanceof SessionEndedError) ended();
    throw e;
  }
}

/** Forgets both tokens on this phone (sign-out, a refused refresh, a session left from the last launch). */
export function forgetTokens() {
  generation++;
  access = null;
  refreshToken = null;
  refreshing = null;
  return SecureStore.deleteItemAsync(REFRESH);
}

/**
 * Ends the server session too (POST /v1/patient/logout, 204), with the tokens held when it is called, so the phone can wipe
 * at once. Best effort: offline, the session ends after 7 days unused, and the phone holds no token for it.
 */
export async function endServerSession() {
  const held = { access, refresh: refreshToken };
  if (!apiBase() || !held.refresh) return;
  const logout = (token: string) => fetch(`${apiBase()}/v1/patient/logout`, { method: 'POST', headers: { Authorization: `Bearer ${token}` } });
  try {
    if (held.access && (await logout(held.access)).status !== 401) return;
    await logout((await postRefresh(held.refresh)).access_token);
  } catch {
    // Offline or already ended: nothing more to do from the phone.
  }
}
