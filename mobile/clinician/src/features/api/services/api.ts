import Constants from 'expo-constants';
import * as SecureStore from 'expo-secure-store';
import { Platform } from 'react-native';
import { apiBase } from './source';
import { deviceOnly } from './store';

/**
 * The staff app's client for Host.Api with native tokens (ADR-004 "native staff app", ADR-011 §2, §3). The refresh token
 * lives in secure-store (this device only), the 15-minute access token in memory only. A 401 refreshes once, one refresh
 * at a time (two with one token would end the session); a refused refresh means the session is over (revoked, replayed,
 * expired) and the app signs out and wipes the phone. Tokens are never logged or put in a URL.
 */
export const REDIRECT_URI = 'medvya-clinician://auth';
const REFRESH = 'medvya.refresh';

let access: string | null = null;
let refreshToken: string | null = null; // the secure-store copy, held so a sign-out can still end the server session after the wipe
let generation = 0; // bumped by forgetTokens: a token answer that arrives after a sign-out is dropped, never stored
let refreshing: Promise<string> | null = null;
let ended = () => {};

/** The server refused the refresh token: the session ended elsewhere (revoked, offboarded, replayed or expired). */
export class SessionEndedError extends Error {
  constructor() { super('mobile.error.generic'); }
}

/** Who to tell when the server ends the session: the session provider, which signs out and wipes. Returns the unsubscribe. */
export function onSessionEnded(listener: () => void) {
  ended = listener;
  return () => { if (ended === listener) ended = () => {}; };
}

const form = (fields: Record<string, string>) => Object.entries(fields).map(([k, v]) => `${k}=${encodeURIComponent(v)}`).join('&');

/** POST /native/token. 400 is the OAuth invalid_grant: the code or refresh token is no good, and will not become good. */
async function postToken(fields: Record<string, string>) {
  const res = await fetch(`${apiBase()}/native/token`, { method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded' }, body: form(fields) });
  if (res.status === 400) throw new SessionEndedError();
  if (!res.ok) throw new Error('mobile.error.generic');
  return (await res.json()) as { access_token: string; refresh_token: string };
}

/** Exchanges and keeps the new pair, unless the phone was signed out meanwhile (then nothing is kept). */
async function grant(fields: Record<string, string>) {
  const since = generation;
  const pair = await postToken(fields);
  if (since !== generation) throw new SessionEndedError();
  await SecureStore.setItemAsync(REFRESH, pair.refresh_token, deviceOnly);
  if (since !== generation) { await SecureStore.deleteItemAsync(REFRESH); throw new SessionEndedError(); } // a wipe ran during the write
  refreshToken = pair.refresh_token;
  access = pair.access_token;
  return access;
}

/** The one-time code from the browser sign-in, with the PKCE verifier, for the first token pair. Platform and version label the device in My devices. */
export const exchangeCode = (code: string, verifier: string) =>
  grant({ grant_type: 'authorization_code', code, code_verifier: verifier, redirect_uri: REDIRECT_URI, platform: Platform.OS, app_version: Constants.expoConfig?.version ?? '0.0.0' });

/** Reads the stored refresh token into memory (at launch, with a stored session); true when there is one. */
export async function loadTokens() {
  refreshToken = await SecureStore.getItemAsync(REFRESH);
  return refreshToken !== null;
}

function refresh() {
  return (refreshing ??= (async () => {
    const token = refreshToken ?? await SecureStore.getItemAsync(REFRESH);
    if (!token) throw new SessionEndedError();
    return grant({ grant_type: 'refresh_token', refresh_token: token });
  })().finally(() => { refreshing = null; }));
}

const call = (path: string, init: RequestInit, token: string) =>
  fetch(`${apiBase()}${path}`, { ...init, headers: { Accept: 'application/json', ...init.headers, Authorization: `Bearer ${token}` } });

/** A call as the signed-in user. A refused refresh signs the phone out (ADR-011 §3: "a revoke, the first 401 from a refresh"). */
export async function send<T>(path: string, init: RequestInit = {}): Promise<T> {
  try {
    let res = await call(path, init, access ?? await refresh());
    if (res.status === 401) res = await call(path, init, await refresh());
    if (!res.ok) throw new Error('mobile.error.generic'); // 403 included: the API, not the app, decides what a role may see
    return (res.status === 204 ? undefined : await res.json()) as T;
  } catch (e) {
    if (e instanceof SessionEndedError) ended();
    throw e;
  }
}

export const post = <T>(path: string, body: unknown) =>
  send<T>(path, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });

/** Forgets both tokens on this phone (sign-out, switch user, failed-PIN limit, a refused refresh). */
export function forgetTokens() {
  generation++;
  access = null;
  refreshToken = null;
  refreshing = null;
  return SecureStore.deleteItemAsync(REFRESH);
}

/**
 * Ends the server session too (POST /bff/logout answers a bearer with 204), with the tokens held when it is called, so the
 * phone can wipe at once. Best effort: offline, the session ends after 7 days unused, and the phone holds no token for it.
 */
export async function endServerSession() {
  const held = { access, refresh: refreshToken };
  if (!apiBase() || !held.refresh) return;
  const logout = (token: string) => fetch(`${apiBase()}/bff/logout`, { method: 'POST', headers: { Authorization: `Bearer ${token}` } });
  try {
    if (held.access && (await logout(held.access)).status !== 401) return;
    await logout((await postToken({ grant_type: 'refresh_token', refresh_token: held.refresh })).access_token);
  } catch {
    // Offline or already ended: nothing more to do from the phone.
  }
}
