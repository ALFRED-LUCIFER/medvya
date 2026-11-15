import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import { forgetTokens, send } from '../features/api/services/api';
import { HomeScreen } from '../features/home/components/HomeScreen';
import { useHomeController } from '../features/home/hooks/useHomeController';
import { ProfileScreen } from '../features/profile/components/ProfileScreen';
import { useProfileController } from '../features/profile/hooks/useProfileController';
import { SignInScreen } from '../features/session/components/SignInScreen';
import { useSession } from '../features/session/hooks/session';
import { useSignInController } from '../features/session/hooks/useSignInController';
import { keychain, WHEN_UNLOCKED_THIS_DEVICE_ONLY } from './fakes';
import { renderApp } from './render';

// MDV-P5-033, ADR-011 §2, §3: the patient app on the server's SMS-code sign-in and native tokens. No records are linked to a
// patient session yet (MDV-P5-036), so every screen says "not connected" instead of showing the synthetic cast.
const API = 'https://api.test';

jest.mock('expo-constants', () => ({ __esModule: true, default: { expoConfig: { version: '1.2.3' } } })); // the device label in My devices
jest.mock('expo-local-authentication', () => ({
  hasHardwareAsync: async () => false, isEnrolledAsync: async () => false, authenticateAsync: async () => ({ success: false }),
  supportedAuthenticationTypesAsync: async () => [], AuthenticationType: { FINGERPRINT: 1, FACIAL_RECOGNITION: 2 },
}));

type Reply = [status: number, body?: unknown];
/** A stand-in for Host.Api's patient sign-in endpoints, as the contract describes them. */
const server = { revoked: false, issued: 0, access: '', code: [202] as Reply, verify: null as Reply | null, calls: [] as { path: string; auth: string | null; body: string }[] };
const reply = (status: number, body?: unknown) => ({ ok: status >= 200 && status < 300, status, json: async () => body });
function issue() {
  server.issued++;
  server.access = `access-${server.issued}`;
  return reply(200, { access_token: server.access, token_type: 'Bearer', expires_in: 900, refresh_token: `refresh-${server.issued}` });
}
function fakeFetch(url: string, init: RequestInit = {}) {
  const auth = ((init.headers ?? {}) as Record<string, string>).Authorization ?? null;
  const path = url.replace(API, '');
  server.calls.push({ path, auth, body: String(init.body ?? '') });
  if (path === '/v1/patient-sign-in/code') return reply(...server.code);
  if (path === '/v1/patient-sign-in/verify') return server.verify ? reply(...server.verify) : issue();
  if (path === '/v1/patient-sign-in/refresh') return server.revoked ? reply(400, { error: 'invalid_grant' }) : issue();
  if (server.revoked || auth !== `Bearer ${server.access}`) return reply(401);
  if (path === '/v1/patient/me') return reply(200, { sessionId: 'session-1', device: 'Medvya app 0.1.0 on iOS', signedInAt: '2026-11-15T05:00:00Z' });
  if (path === '/v1/patient/logout') return reply(204);
  return reply(404);
}
const bodyOf = (path: string) => JSON.parse(server.calls.find(c => c.path === path)!.body) as Record<string, unknown>;

/** The app's routes in small: sign-in, or Home and Profile once signed in. */
function App() {
  const { session } = useSession();
  return session ? <><Home /><Profile /></> : <SignIn />;
}
const SignIn = () => <SignInScreen {...useSignInController()} />;
const Home = () => <HomeScreen {...useHomeController()} />;
const Profile = () => <ProfileScreen {...useProfileController()} />;

async function toCode() {
  await renderApp(<App />);
  await fireEvent.press(screen.getByRole('button', { name: 'Skip' }));
  await fireEvent.changeText(screen.getByLabelText('Mobile number'), '90000 00101');
  await fireEvent.press(screen.getByRole('button', { name: 'Send code' }));
}
async function signIn() {
  await toCode();
  await fireEvent.changeText(await screen.findByLabelText('6-digit code'), '123456');
  await fireEvent.press(screen.getByRole('button', { name: 'Continue' }));
}

beforeEach(() => {
  process.env.EXPO_PUBLIC_MEDVYA_API = API;
  Object.assign(server, { revoked: false, issued: 0, access: '', code: [202], verify: null, calls: [] });
  globalThis.fetch = jest.fn(fakeFetch) as unknown as typeof fetch;
});
afterEach(async () => {
  await forgetTokens();
  keychain.clear();
  delete process.env.EXPO_PUBLIC_MEDVYA_API;
});

it('signs in with the SMS code, keeps the refresh token on this device only and proves the session', async () => {
  await signIn();
  // No records are linked yet, so there is no one to choose: the code leads to the unlock step.
  expect(await screen.findByText('Use Face ID or Touch ID?')).toBeOnTheScreen();
  expect(screen.queryByText('Who are you?')).toBeNull();
  await fireEvent.press(screen.getByRole('button', { name: 'Continue' }));
  expect(await screen.findAllByText('This build is not connected to Medvya yet.')).toHaveLength(2); // Home and Profile, never cast data
  expect(screen.queryByText(/Meera/)).toBeNull();

  expect(bodyOf('/v1/patient-sign-in/code')).toEqual({ countryCode: 'IN', mobile: '+919000000101', language: 'en' });
  expect(bodyOf('/v1/patient-sign-in/verify')).toEqual({ countryCode: 'IN', mobile: '+919000000101', code: '123456', platform: 'ios', appVersion: '1.2.3' });
  expect(keychain.get('medvya.refresh')).toEqual({ value: 'refresh-1', options: { keychainAccessible: WHEN_UNLOCKED_THIS_DEVICE_ONLY } });
  expect(server.calls.find(c => c.path === '/v1/patient/me')?.auth).toBe('Bearer access-1');
  expect(server.calls.every(c => !/access-|refresh-/.test(c.path))).toBe(true); // tokens never in a URL
});

it.each<[string, 'code' | 'verify', Reply, string]>([
  ['the number is locked', 'code', [429, { error: 'locked', retryAfterMinutes: 15 }], 'Too many wrong codes. Try again in 15 minutes.'],
  ['too many codes were sent', 'code', [429, { error: 'too_many_codes', retryAfterMinutes: 30 }], 'Too many codes were sent to this number. Try again in 30 minutes.'],
  ['the SMS could not be sent', 'code', [503, { error: 'send_failed' }], 'We could not send the code just now. Try again in a little while.'],
  ['the code is wrong', 'verify', [400, { error: 'wrong_code' }], 'That code did not work. Check the app and try again.'],
  ['five wrong codes locked the number', 'verify', [429, { error: 'locked', retryAfterMinutes: 1 }], 'Too many wrong codes. Try again in 1 minute.'],
])('says in plain words when %s, and keeps no token', async (_, endpoint, answer, words) => {
  server[endpoint] = answer;
  if (endpoint === 'code') await toCode(); else await signIn();
  expect(await screen.findByText(words)).toBeOnTheScreen();
  expect(keychain.size).toBe(0);
});

it('signs out and wipes the phone when the server refuses the refresh (revoked, replayed or expired)', async () => {
  await signIn();
  await fireEvent.press(await screen.findByRole('button', { name: 'Continue' }));
  expect(await screen.findAllByText('This build is not connected to Medvya yet.')).toHaveLength(2);
  expect(keychain.size).toBe(1);

  server.revoked = true; // My devices or a replay ended the session on the server
  await act(async () => { await send('/v1/patient/me').catch(() => undefined); });
  expect(await screen.findByText('Your clinic, in your pocket')).toBeOnTheScreen();
  await waitFor(() => expect(keychain.size).toBe(0));
});

it('ends the server session and wipes the token on sign-out', async () => {
  await signIn();
  await fireEvent.press(await screen.findByRole('button', { name: 'Continue' }));
  await fireEvent.press(await screen.findByRole('button', { name: 'Sign out' }));
  expect(await screen.findByText('Your clinic, in your pocket')).toBeOnTheScreen();
  await waitFor(() => expect(server.calls.find(c => c.path === '/v1/patient/logout')?.auth).toBe('Bearer access-1'));
  expect(keychain.size).toBe(0);
});

it('at launch, ends and wipes a session left on the phone by an app that was closed', async () => {
  keychain.set('medvya.refresh', { value: 'refresh-0' });
  await renderApp(<App />);
  await waitFor(() => expect(keychain.size).toBe(0));
  expect(JSON.parse(server.calls.find(c => c.path === '/v1/patient-sign-in/refresh')!.body)).toEqual({ refreshToken: 'refresh-0' });
  await waitFor(() => expect(server.calls.find(c => c.path === '/v1/patient/logout')?.auth).toBe('Bearer access-1'));
});
