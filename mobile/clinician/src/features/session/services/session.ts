import * as LocalAuthentication from 'expo-local-authentication';
import { Platform } from 'react-native';
import { fromCast } from '../../api/services/source';
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

/** The phone's face or fingerprint unlock, falling back to the device passcode. */
export async function unlockWithBiometrics(prompt: string) {
  const r = await LocalAuthentication.authenticateAsync({ promptMessage: prompt });
  return r.success;
}

/** What this phone can unlock with, in the words its platform uses. */
export async function phoneUnlockMethod() {
  const types = await LocalAuthentication.supportedAuthenticationTypesAsync();
  const { AuthenticationType } = LocalAuthentication;
  return unlockMethod(Platform.OS, { face: types.includes(AuthenticationType.FACIAL_RECOGNITION), finger: types.includes(AuthenticationType.FINGERPRINT) });
}
export const platformUnlockMethod = unlockMethod(Platform.OS, { face: false, finger: false });
