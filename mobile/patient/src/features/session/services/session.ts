import type { Country, Locale } from '@medvya/i18n';
import Constants from 'expo-constants';
import * as LocalAuthentication from 'expo-local-authentication';
import { Platform } from 'react-native';
import { endServerSession, forgetTokens, loadTokens, postAnonymous, send, signIn, type TokenPair } from '../../api/services/api';
import { apiBase, fromCast } from '../../api/services/source';
import { SignInWaitError, unlockMethod, type Clinic, type Person } from '../domain/session';

/** A number as the sign-in endpoints take it: the country picked and the E.164 number. */
export type Mobile = { country: Country; phone: string };

type Refusal = { error?: string; retryAfterMinutes?: number };
const refusal = async (res: Response) => (await res.json().catch(() => ({}))) as Refusal;

/** A sign-in answer that is not a success, in the patient's words: wait N minutes, could not send, or try again. */
function refused(status: number, { error, retryAfterMinutes }: Refusal): never {
  if (status === 429 && typeof retryAfterMinutes === 'number')
    throw new SignInWaitError(error === 'too_many_codes' ? 'mobile.patientSignIn.tooManyCodes' : 'mobile.patientSignIn.locked', retryAfterMinutes);
  if (status === 503) throw new Error('mobile.patientSignIn.sendFailed');
  if (status === 400) throw new Error('mobile.patientSignIn.phoneInvalid');
  throw new Error('mobile.error.generic');
}

/** Sends the SMS code. Every valid number gets the same answer, so the screen cannot reveal who has records. */
export async function requestCode({ country, phone }: Mobile, language: Locale) {
  if (!apiBase()) return fromCast(() => ({ sent: true }));
  const res = await postAnonymous('/v1/patient-sign-in/code', { countryCode: country, mobile: phone, language });
  if (!res.ok) refused(res.status, await refusal(res));
  return { sent: true };
}

/**
 * The people this number may act for and their clinic, or null for a wrong code. With a server the session holds no
 * people or clinic yet (records are linked in MDV-P5-036): GET /v1/patient/me proves the new session works.
 * In Development without a server any six-digit code works for a cast number, and the cast's locked number answers as the API does.
 */
export async function verifyCode({ country, phone }: Mobile, code: string): Promise<{ people: Person[]; clinic: Clinic | null } | null> {
  if (!apiBase()) return fromCast(c => {
    if (c.lockedNumber.phone === phone) throw new SignInWaitError('mobile.patientSignIn.locked', c.lockedNumber.retryAfterMinutes);
    const account = c.accounts.find(a => a.phone === phone);
    return account ? { people: c.people.filter(p => account.people.includes(p.id)) as Person[], clinic: c.clinic as Clinic } : null;
  });
  const issued = await signIn(async () => {
    const res = await postAnonymous('/v1/patient-sign-in/verify', {
      countryCode: country, mobile: phone, code, platform: Platform.OS, appVersion: Constants.expoConfig?.version ?? '0.0.0',
    });
    if (res.ok) return (await res.json()) as TokenPair;
    const body = await refusal(res);
    if (res.status === 400 && body.error === 'wrong_code') return null;
    return refused(res.status, body);
  });
  if (!issued) return null;
  try {
    await send('/v1/patient/me');
  } catch (e) {
    await signOutDevice(); // a session the app cannot use is ended, not left behind on the phone
    throw e;
  }
  return { people: [], clinic: null };
}

/** Sign-out: the server session ends too (best effort, with the tokens held now) while the phone forgets them at once. */
export const signOutDevice = () => Promise.all([endServerSession(), forgetTokens()]);

/**
 * At launch: a refresh token left from a session the app no longer holds (the session lives in memory) is ended and wiped.
 * shortcut: closing the app signs the patient out; reopening locked with the stored token comes with linked records (MDV-P5-036).
 */
export const endLeftoverSession = async () => { if (await loadTokens()) await signOutDevice(); };

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
