import * as Crypto from 'expo-crypto';
import * as LocalAuthentication from 'expo-local-authentication';
import * as ScreenCapture from 'expo-screen-capture';
import * as SecureStore from 'expo-secure-store';
import * as WebBrowser from 'expo-web-browser';
import { Platform } from 'react-native';
import { endServerSession, exchangeCode, forgetTokens, loadTokens, post, REDIRECT_URI, send } from '../../api/services/api';
import { apiBase, fromCast } from '../../api/services/source';
import { deviceOnly, hex, purgeExpired, wipeStore } from '../../api/services/store';
import { initialsOf, roleOf, unlockMethod, type Facility, type Session, type Staff } from '../domain/session';

/** Whether sign-in goes through the server (system browser) or, in a Development build without one, the synthetic cast. */
export const signsInThroughServer = () => apiBase() !== null;

/** Starts sign-in for a work email. Unknown emails get the same answer as known ones, so the screen cannot be used to find accounts. */
export const startSignIn = (email: string) => fromCast(c => ({ known: c.staff.some(s => s.email === email) }));

/** Development cast only: any six-digit code signs in a cast member. With a server, signInWithBrowser is the sign-in. */
export async function verifyCode(email: string, _code: string): Promise<Staff | null> {
  return fromCast(c => c.staff.find(s => s.email === email) ?? null);
}

// GET /v1/me/context (ADR-004): the fields the app reads.
type Context = {
  user: { id: string; displayName: string };
  memberships?: { id: string; facilityId: string; facilityName: string; tenantName: string; role: string }[];
  active?: { membershipId: string; role: string; timeZone?: string | null } | null;
};
const context = () => send<Context>('/v1/me/context');

const base64Url = (b64: string) => b64.replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');

/**
 * Staff sign-in through the server (MDV-P5-033): the system browser runs the normal staff sign-in (MFA, Medvya accounts
 * only), bound to this app by PKCE S256, and comes back to medvya-clinician://auth with a one-time code that only this
 * verifier can exchange. Null when the person closed the browser. The role is the facility's, set when one is chosen.
 */
export async function signInWithBrowser(): Promise<Staff | null> {
  const verifier = hex(Crypto.getRandomBytes(32)); // 64 characters, inside RFC 7636's 43–128
  const challenge = base64Url(await Crypto.digestStringAsync(Crypto.CryptoDigestAlgorithm.SHA256, verifier, { encoding: Crypto.CryptoEncoding.BASE64 }));
  const query = `redirect_uri=${encodeURIComponent(REDIRECT_URI)}&code_challenge=${challenge}&code_challenge_method=S256`;
  // Ephemeral on iOS: the browser keeps no sign-in cookie for the next person on a shared phone (ADR-011 §5).
  const result = await WebBrowser.openAuthSessionAsync(`${apiBase()}/native/login?${query}`, REDIRECT_URI, { preferEphemeralSession: true });
  if (result.type !== 'success') return null;
  const code = /[?&]code=([^&#]+)/.exec(result.url)?.[1];
  if (!code) throw new Error('mobile.error.generic');
  await exchangeCode(decodeURIComponent(code), verifier);
  const { user } = await context();
  return { id: user.id, name: user.displayName, initials: initialsOf(user.displayName), email: '', role: 'frontDesk' };
}

export async function listFacilities(_staffId: string): Promise<Facility[]> {
  if (!signsInThroughServer()) return fromCast(c => c.facilities);
  return ((await context()).memberships ?? []).map(m => ({ id: m.facilityId, membershipId: m.id, name: m.facilityName, city: m.tenantName, timeZone: '' }));
}

/**
 * Works at `facility` from now on. Through the server: POST /v1/me/facility re-scopes the session; the old access token is
 * then refused and the client refreshes on the next call, which reads the role and time zone of that membership.
 */
export async function chooseFacility(staff: Staff, facility: Facility): Promise<Session> {
  if (!signsInThroughServer()) return { staff, facility };
  await post('/v1/me/facility', { membershipId: facility.membershipId });
  const { active } = await context();
  if (!active || active.membershipId !== facility.membershipId || !active.timeZone) throw new Error('mobile.error.generic');
  return { staff: { ...staff, role: roleOf(active.role) }, facility: { ...facility, timeZone: active.timeZone } };
}

/**
 * Who is signed in and how they unlock, so the app reopens locked instead of signed out. It holds the staff member's own
 * name and workplace, no patient data; the tokens are kept apart (api.ts).
 */
const PROFILE = 'medvya.profile';
export const saveProfile = (session: Session, biometric: boolean) => SecureStore.setItemAsync(PROFILE, JSON.stringify({ session, biometric }), deviceOnly);

/** The stored session, or null when there is none, or when a server session has lost its refresh token (it cannot go on). */
export async function restoreProfile(): Promise<{ session: Session; biometric: boolean } | null> {
  const stored = await SecureStore.getItemAsync(PROFILE);
  if (!stored || (signsInThroughServer() && !(await loadTokens()))) return null;
  await purgeExpired();
  return JSON.parse(stored) as { session: Session; biometric: boolean };
}

export async function biometricsAvailable() {
  return (await LocalAuthentication.hasHardwareAsync()) && (await LocalAuthentication.isEnrolledAsync());
}

/**
 * The phone's face or fingerprint unlock. The fallback is the app PIN, not the phone's passcode, which others on a
 * shared phone may know: "Use PIN" or Cancel returns false and the lock screen shows the PIN field.
 */
export async function unlockWithBiometrics(prompt: string, pinLabel: string) {
  const r = await LocalAuthentication.authenticateAsync({ promptMessage: prompt, disableDeviceFallback: true, fallbackLabel: pinLabel });
  return r.success;
}

const PIN = 'medvya.pin';
const pinHash = (salt: string, pin: string) => Crypto.digestStringAsync(Crypto.CryptoDigestAlgorithm.SHA256, `${salt}:${pin}`);

/**
 * Keeps the app PIN's check value (salt and hash, never the PIN) in secure-store (ADR-011 §3).
 * shortcut: a 6-digit PIN's hash falls to brute force for anyone who can read the Keychain/Keystore item; the
 * device-only Keychain and the 5-try wipe are the controls. With native tokens (MDV-P5-033) the server checks the PIN and counts tries (owner decision 15 Nov 2026).
 */
export async function setPin(pin: string) {
  const salt = Crypto.randomUUID();
  await SecureStore.setItemAsync(PIN, `${salt}:${await pinHash(salt, pin)}`, deviceOnly);
}

export async function checkPin(pin: string) {
  const [salt, hash] = (await SecureStore.getItemAsync(PIN))?.split(':') ?? [];
  return !!salt && (await pinHash(salt, pin)) === hash;
}

/** Everything this app keeps on the phone goes: the encrypted last copy, its key, the PIN, the tokens and who was signed in (ADR-011 §3, §5). */
export const wipeDevice = () => Promise.all([wipeStore(), SecureStore.deleteItemAsync(PIN), SecureStore.deleteItemAsync(PROFILE), forgetTokens()]);

/** Sign-out: the server session ends too (best effort, with the tokens held now) while the phone wipes at once. */
export const signOutDevice = () => Promise.all([endServerSession(), wipeDevice()]);

/**
 * ADR-011 §6 for the staff app: screenshots and screen recording are blocked on both platforms (FLAG_SECURE on
 * Android, which also blanks the recent-apps preview; the secure-field method on iOS 13+), and iOS blurs the app switcher.
 */
export const protectScreen = () => Promise.all([ScreenCapture.preventScreenCaptureAsync(), Platform.OS === 'ios' && ScreenCapture.enableAppSwitcherProtectionAsync()]);

/** What this phone can unlock with, in the words its platform uses. */
export async function phoneUnlockMethod() {
  const types = await LocalAuthentication.supportedAuthenticationTypesAsync();
  const { AuthenticationType } = LocalAuthentication;
  return unlockMethod(Platform.OS, { face: types.includes(AuthenticationType.FACIAL_RECOGNITION), finger: types.includes(AuthenticationType.FINGERPRINT) });
}
export const platformUnlockMethod = unlockMethod(Platform.OS, { face: false, finger: false });
