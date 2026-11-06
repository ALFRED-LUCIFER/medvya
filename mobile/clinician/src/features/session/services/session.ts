import * as LocalAuthentication from 'expo-local-authentication';
import { fromCast } from '../../api/services/source';
import type { Facility, Staff } from '../domain/session';

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

/** Face ID or fingerprint, falling back to the device passcode. */
export async function unlockWithBiometrics(prompt: string) {
  const r = await LocalAuthentication.authenticateAsync({ promptMessage: prompt });
  return r.success;
}
