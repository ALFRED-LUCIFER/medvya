import * as Crypto from 'expo-crypto';
import * as LocalAuthentication from 'expo-local-authentication';
import * as ScreenCapture from 'expo-screen-capture';
import * as SecureStore from 'expo-secure-store';
import { Platform } from 'react-native';
import { fromCast } from '../../api/services/source';
import { deviceOnly, wipeStore } from '../../api/services/store';
import { unlockMethod, type Facility, type Staff } from '../domain/session';

/** Starts sign-in for a work email. Unknown emails get the same answer as known ones, so the screen cannot be used to find accounts. */
export const startSignIn = (email: string) => fromCast(c => ({ known: c.staff.some(s => s.email === email) }));

/**
 * shortcut: in Development any six-digit code signs in a cast member; the real check (authenticator code or passkey
 * against Entra, then native tokens) arrives with MDV-P5-033.
 */
export async function verifyCode(email: string, _code: string): Promise<Staff | null> {
  return fromCast(c => c.staff.find(s => s.email === email) ?? null);
}

export const listFacilities = (_staffId: string): Promise<Facility[]> => fromCast(c => c.facilities);

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
 * device-only Keychain and the 5-try wipe are the controls. Bind the PIN to the device key with native tokens (MDV-P5-033).
 */
export async function setPin(pin: string) {
  const salt = Crypto.randomUUID();
  await SecureStore.setItemAsync(PIN, `${salt}:${await pinHash(salt, pin)}`, deviceOnly);
}

export async function checkPin(pin: string) {
  const [salt, hash] = (await SecureStore.getItemAsync(PIN))?.split(':') ?? [];
  return !!salt && (await pinHash(salt, pin)) === hash;
}

/** Everything this app keeps on the phone goes: the encrypted last copy, its key and the PIN (ADR-011 §3, §5). */
export const wipeDevice = () => Promise.all([wipeStore(), SecureStore.deleteItemAsync(PIN)]);

/**
 * ADR-011 §6 for the staff app. Android: FLAG_SECURE blocks screenshots and screen recording, and blanks the
 * recent-apps preview. iOS: the app-switcher preview is blurred.
 * shortcut: iOS screenshots are not yet detected and audited: there is no audit endpoint for device events. Add
 * ScreenCapture.addScreenshotListener with that endpoint over native tokens (MDV-P5-033).
 */
export const protectScreen = () => (Platform.OS === 'android' ? ScreenCapture.preventScreenCaptureAsync() : ScreenCapture.enableAppSwitcherProtectionAsync());

/** What this phone can unlock with, in the words its platform uses. */
export async function phoneUnlockMethod() {
  const types = await LocalAuthentication.supportedAuthenticationTypesAsync();
  const { AuthenticationType } = LocalAuthentication;
  return unlockMethod(Platform.OS, { face: types.includes(AuthenticationType.FACIAL_RECOGNITION), finger: types.includes(AuthenticationType.FINGERPRINT) });
}
export const platformUnlockMethod = unlockMethod(Platform.OS, { face: false, finger: false });
