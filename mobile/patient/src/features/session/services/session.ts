import * as LocalAuthentication from 'expo-local-authentication';
import { Platform } from 'react-native';
import { fromCast } from '../../api/services/source';
import { SignInLockedError, unlockMethod, type Clinic, type Person } from '../domain/session';

/** Sends the SMS code. Every valid number gets the same answer, so the screen cannot reveal who has records. */
export const requestCode = (_phone: string) => fromCast(() => ({ sent: true }));

/**
 * shortcut: in Development any six-digit code works for a cast number, and the cast's locked number answers as the API does after five
 * wrong codes; call POST /v1/patient-sign-in/code and /verify (429 locked → SignInLockedError) once patient sessions exist (MDV-P5-033).
 */
export const verifyCode = (phone: string, _code: string) => fromCast(c => {
  if (c.lockedNumber.phone === phone) throw new SignInLockedError(c.lockedNumber.retryAfterMinutes);
  const account = c.accounts.find(a => a.phone === phone);
  return account ? { people: c.people.filter(p => account.people.includes(p.id)) as Person[], clinic: c.clinic as Clinic } : null;
});

export async function biometricsAvailable() {
  return (await LocalAuthentication.hasHardwareAsync()) && (await LocalAuthentication.isEnrolledAsync());
}
export async function unlockWithBiometrics(prompt: string) {
  return (await LocalAuthentication.authenticateAsync({ promptMessage: prompt })).success;
}

/** What this phone can unlock with, in the words its platform uses. */
export async function phoneUnlockMethod() {
  const types = await LocalAuthentication.supportedAuthenticationTypesAsync();
  const { AuthenticationType } = LocalAuthentication;
  return unlockMethod(Platform.OS, { face: types.includes(AuthenticationType.FACIAL_RECOGNITION), finger: types.includes(AuthenticationType.FINGERPRINT) });
}
export const platformUnlockMethod = unlockMethod(Platform.OS, { face: false, finger: false });
