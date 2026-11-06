import * as LocalAuthentication from 'expo-local-authentication';
import { fromCast } from '../../api/services/source';
import type { Clinic, Person } from '../domain/session';

/** Sends the SMS code. Every valid number gets the same answer, so the screen cannot reveal who has records. */
export const requestCode = (_phone: string) => fromCast(() => ({ sent: true }));

/**
 * shortcut: in Development any six-digit code works for a cast number; the SMS code, lockout after five tries and
 * native tokens arrive with MDV-P5-033.
 */
export const verifyCode = (phone: string, _code: string) => fromCast(c => {
  const account = c.accounts.find(a => a.phone === phone);
  return account ? { people: c.people.filter(p => account.people.includes(p.id)) as Person[], clinic: c.clinic as Clinic } : null;
});

export async function biometricsAvailable() {
  return (await LocalAuthentication.hasHardwareAsync()) && (await LocalAuthentication.isEnrolledAsync());
}
export async function unlockWithBiometrics(prompt: string) {
  return (await LocalAuthentication.authenticateAsync({ promptMessage: prompt })).success;
}
