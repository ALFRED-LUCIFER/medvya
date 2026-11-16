import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import { useEffect, useRef } from 'react';
import { Text } from 'react-native';
import { exchangeCode, forgetTokens, send } from '../features/api/services/api';
import { readCopy } from '../features/api/services/store';
import { LockScreen } from '../features/session/components/LockScreen';
import { SignInScreen } from '../features/session/components/SignInScreen';
import type { Session } from '../features/session/domain/session';
import { useSession } from '../features/session/hooks/session';
import { useLockController } from '../features/session/hooks/useLockController';
import { useSignInController } from '../features/session/hooks/useSignInController';
import { saveProfile, setPin, wipeDevice } from '../features/session/services/session';
import { TodayScreen } from '../features/today/components/TodayScreen';
import { useTodayController } from '../features/today/hooks/useTodayController';
import { files, keychain } from './fakes';
import { renderApp } from './render';

// MDV-P5-034 AC 6, ADR-011 §2, §3, §5: the staff app on the server's native tokens. A refused refresh (revoke, replay,
// expiry) or Switch user leaves nothing of the previous user; a stored session survives a relaunch, locked.
const API = 'https://api.test';
const doctorId = '0b6f6a52-0000-4000-8000-000000000001', facilityId = '0b6f6a52-0000-4000-8000-0000000000f1', membershipId = '0b6f6a52-0000-4000-8000-0000000000a1';
const doctor: Session = {
  staff: { id: doctorId, name: 'Dr. Arjun Rao (synthetic)', initials: 'AR', email: '', role: 'doctor' },
  facility: { id: facilityId, membershipId, name: 'Adyar (synthetic)', city: 'Iyer Family Clinic (synthetic)', timeZone: 'Asia/Kolkata' },
};

let mockBrowserUrl: string | null = null;
let mockBrowserOpened = '';
jest.mock('expo-web-browser', () => ({
  openAuthSessionAsync: async (url: string) => { mockBrowserOpened = url; return mockBrowserUrl ? { type: 'success', url: mockBrowserUrl } : { type: 'cancel' }; },
}));
jest.mock('expo-constants', () => ({ __esModule: true, default: { expoConfig: { version: '1.2.3' } } })); // the device label in My devices
jest.mock('expo-local-authentication', () => ({
  hasHardwareAsync: async () => false, isEnrolledAsync: async () => false, authenticateAsync: async () => ({ success: false }),
  supportedAuthenticationTypesAsync: async () => [], AuthenticationType: { FINGERPRINT: 1, FACIAL_RECOGNITION: 2 },
}));

/** A stand-in for Host.Api's native endpoints (NativeAuthentication.cs), as the integration tests drive them. */
const server = { revoked: false, issued: 0, access: '', calls: [] as { url: string; method: string; auth: string | null; body: string }[], during: undefined as (() => void) | undefined };
const reply = (status: number, body?: unknown) => ({ ok: status < 300, status, json: async () => body });
function fakeFetch(url: string, init: RequestInit = {}) {
  const headers = (init.headers ?? {}) as Record<string, string>;
  const auth = headers.Authorization ?? null, body = String(init.body ?? ''), method = init.method ?? 'GET';
  server.calls.push({ url, method, auth, body });
  const path = url.replace(API, '');
  if (path === '/native/token') {
    if (server.revoked) return reply(400, { error: 'invalid_grant' });
    server.during?.(); // something the phone does while the answer is on its way
    server.issued++;
    server.access = `access-${server.issued}`;
    return reply(200, { access_token: server.access, token_type: 'Bearer', expires_in: 900, refresh_token: `refresh-${server.issued}` });
  }
  if (server.revoked || auth !== `Bearer ${server.access}`) return reply(401);
  if (path === '/bff/logout') return reply(204);
  if (path === '/v1/me/context') return reply(200, {
    user: { id: doctorId, displayName: 'Dr. Arjun Rao (synthetic)' },
    memberships: [{ id: membershipId, tenantId: 't', tenantName: 'Iyer Family Clinic (synthetic)', facilityId, facilityName: 'Adyar (synthetic)', role: 'SoloDoctor' }],
    active: { membershipId, role: 'SoloDoctor', timeZone: 'Asia/Kolkata' },
  });
  if (path === '/v1/me/facility') { server.access = 'stale'; return reply(204); } // the old access token names no facility now
  if (path.startsWith('/v1/front-desk/board?')) return reply(200, {
    facilityId, timeZone: 'Asia/Kolkata', date: '2026-11-15', now: '2026-11-15T05:00:00Z',
    cards: [
      { patientId: 'p-meera', patientName: 'Meera Iyer', dateOfBirth: '1972-03-01', sex: 'Female', calendarId: doctorId, reasonCategory: 'ChronicReview',
        start: '2026-11-15T04:30:00Z', state: 'ReadyForDoctor', room: 'Room 3', token: 'A-07', queuePlace: 1 },
      { patientId: 'p-joseph', patientName: 'Joseph Mathew', dateOfBirth: null, sex: null, calendarId: doctorId, reasonCategory: 'General',
        start: '2026-11-15T05:30:00Z', state: 'Booked', room: null, token: null },
      { patientId: 'p-other', patientName: 'Kavya Menon', dateOfBirth: '1995-01-01', sex: 'Female', calendarId: 'another-doctor', reasonCategory: 'General',
        start: '2026-11-15T04:00:00Z', state: 'CheckedIn', room: null, token: 'A-05' },
    ],
  });
  if (path === '/v1/notes/pending') return reply(200, { items: [{}, {}], more: false });
  if (path === '/v1/chart/allergy-flags') return reply(200, { items: [{ patientId: 'p-meera', state: 'Listed', abnormalVitals: false }] });
  return reply(404);
}

const noop = () => {};
function Today() {
  return <TodayScreen {...useTodayController()} onOpenAccount={noop} onOpenPatient={noop} onStartConsult={noop} />;
}
function Lock() {
  return <LockScreen {...useLockController()} />;
}
function SignIn() {
  return <SignInScreen {...useSignInController()} />;
}
/** The app's routes in small: sign-in, lock or Today. `start` signs the doctor in once the launch check is done. */
function App({ start }: { start?: Session }) {
  const { ready, session, locked, begin } = useSession();
  const began = useRef(false);
  useEffect(() => { if (ready && start && !began.current) { began.current = true; begin(start, false); } }, [ready, start, begin]); // once, after the launch check
  if (!ready) return null;
  if (!session) return start ? <Text>signed out</Text> : <SignIn />;
  return locked ? <Lock /> : <Today />;
}

const log = { log: jest.spyOn(console, 'log'), info: jest.spyOn(console, 'info'), warn: jest.spyOn(console, 'warn'), error: jest.spyOn(console, 'error'), debug: jest.spyOn(console, 'debug') };
beforeEach(() => {
  process.env.EXPO_PUBLIC_MEDVYA_API = API;
  Object.assign(server, { revoked: false, issued: 0, access: '', calls: [], during: undefined });
  globalThis.fetch = jest.fn(fakeFetch) as unknown as typeof fetch;
  mockBrowserUrl = null;
});
afterEach(async () => {
  await wipeDevice();
  delete process.env.EXPO_PUBLIC_MEDVYA_API;
});

/** A doctor signed in on this phone before: the refresh token, who it is, a PIN. */
async function storedSession() {
  keychain.set('medvya.refresh', 'refresh-0');
  await saveProfile(doctor, false);
  await setPin('246810');
}

it('signs in through the system browser with PKCE, chooses the facility and takes its role', async () => {
  mockBrowserUrl = 'medvya-clinician://auth?code=one%2Btime';
  await renderApp(<App />);
  await fireEvent.press(await screen.findByRole('button', { name: 'Sign in' }));
  await fireEvent.press(await screen.findByRole('button', { name: /Adyar \(synthetic\)/ }));
  await fireEvent.changeText(await screen.findByLabelText('App PIN'), '246810');
  await fireEvent.changeText(screen.getByLabelText('Enter the PIN again'), '246810');
  await fireEvent.press(screen.getByRole('button', { name: 'Continue' }));
  await fireEvent.press(await screen.findByRole('button', { name: 'Continue' }));
  expect(await screen.findByText('54 F · Long-term condition review · Room 3')).toBeOnTheScreen();

  // The login URL carries the S256 challenge of the verifier the token call sends, and the allow-listed redirect.
  const login = new URL(mockBrowserOpened);
  expect(login.pathname).toBe('/native/login');
  expect(login.searchParams.get('redirect_uri')).toBe('medvya-clinician://auth');
  expect(login.searchParams.get('code_challenge_method')).toBe('S256');
  const exchange = new URLSearchParams(server.calls.find(c => c.url.endsWith('/native/token'))!.body);
  expect(Object.fromEntries(exchange)).toEqual({ grant_type: 'authorization_code', code: 'one+time', code_verifier: expect.stringMatching(/^[0-9a-f]{64}$/), redirect_uri: 'medvya-clinician://auth', platform: 'ios', app_version: '1.2.3' });
  const { createHash } = jest.requireActual<{ createHash: (a: string) => { update: (s: string) => { digest: (e: 'base64url') => string } } }>('node:crypto');
  expect(login.searchParams.get('code_challenge')).toBe(createHash('sha256').update(exchange.get('code_verifier')!).digest('base64url'));

  // The facility choice re-scoped the session; the app refreshed once and kept the newest refresh token, on this device only.
  expect(server.calls.filter(c => c.url.endsWith('/v1/me/facility'))[0]!.body).toBe(JSON.stringify({ membershipId }));
  expect(keychain.get('medvya.refresh')).toBe(`refresh-${server.issued}`);
  expect(screen.getByText('Allergies listed')).toBeOnTheScreen();
});

it('sends the bearer token on each call, never in a URL or a log', async () => {
  await storedSession();
  await renderApp(<App start={doctor} />);
  expect(await screen.findByText('54 F · Long-term condition review · Room 3')).toBeOnTheScreen();
  const board = server.calls.find(c => c.url.includes('/v1/front-desk/board'))!;
  expect(board.auth).toBe('Bearer access-1');
  expect(server.calls.every(c => !/access-|refresh-/.test(c.url))).toBe(true);
  const logged = JSON.stringify(Object.values(log).flatMap(spy => spy.mock.calls));
  expect(logged).not.toMatch(/access-\d|refresh-\d/);
});

it('shows a doctor their own visits from the board, with blanks for what the server does not know', async () => {
  await storedSession();
  await renderApp(<App start={doctor} />);
  expect(await screen.findByText('54 F · Long-term condition review · Room 3')).toBeOnTheScreen();
  expect(screen.queryByText('Kavya Menon')).toBeNull(); // another doctor's patient
  expect(screen.getByText('54 F · Long-term condition review · Room 3')).toBeOnTheScreen();
  expect(screen.getByText('· 11:00')).toBeOnTheScreen(); // no date of birth or sex on record: blank, not guessed
});

it('signs out and wipes the phone when the server refuses the refresh (revoked)', async () => {
  await storedSession();
  await renderApp(<App start={doctor} />);
  expect(await screen.findByText('54 F · Long-term condition review · Room 3')).toBeOnTheScreen();
  expect(files.size).toBe(1); // the encrypted last copy of Today
  expect(await readCopy(`today:${facilityId}`)).not.toBeNull();

  server.revoked = true; // My devices, offboarding or a replay ended the session on the server
  await act(async () => { await send('/v1/me/context').catch(noop); });
  expect(await screen.findByText('signed out')).toBeOnTheScreen();
  await waitFor(() => { expect(files.size).toBe(0); expect(keychain.size).toBe(0); });
});

it('wipes the phone and ends the server session on Switch user', async () => {
  await storedSession();
  await renderApp(<App />); // relaunch: the stored session opens locked
  await fireEvent.press(await screen.findByRole('button', { name: 'Switch user' }));
  expect(await screen.findByRole('button', { name: 'Sign in' })).toBeOnTheScreen();
  await waitFor(() => { expect(files.size).toBe(0); expect(keychain.size).toBe(0); });
  await waitFor(() => expect(server.calls.find(c => c.url.endsWith('/bff/logout'))?.auth).toBe('Bearer access-1'));
});

it('wipes at launch a stored profile whose refresh token is gone', async () => {
  await saveProfile(doctor, false);
  await setPin('246810');
  await renderApp(<App />);
  expect(await screen.findByRole('button', { name: 'Sign in' })).toBeOnTheScreen();
  await waitFor(() => expect(keychain.size).toBe(0));
});

it('ends on the server a token pair that arrives after the phone signed out, and keeps none of it', async () => {
  server.during = () => { forgetTokens(); }; // Switch user while the code exchange is out
  await expect(exchangeCode('code-1', 'verifier-1')).rejects.toThrow();
  expect(server.calls.find(c => c.url.endsWith('/bff/logout'))?.auth).toBe('Bearer access-1');
  expect(keychain.size).toBe(0);
});

it('runs one refresh for calls that need it at the same time (two would end the session)', async () => {
  keychain.set('medvya.refresh', 'refresh-0');
  await Promise.all([send('/v1/me/context'), send('/v1/me/context'), send('/v1/me/context')]);
  expect(server.calls.filter(c => c.url.endsWith('/native/token'))).toHaveLength(1);
});
