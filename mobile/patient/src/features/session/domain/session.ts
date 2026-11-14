import { z } from 'zod';
import type { Country } from '@medvya/i18n';

/** A person this number may act for: the patient themself, or family the clinic has linked (explicit, time-limited proxy). */
export type Person = { id: string; name: string; initials: string; relation: 'self' | 'family'; clinic: string; since: string; linked: boolean; proxyUntil?: string };
export type Clinic = { name: string; branch: string; phone: string; address: string; country: Country; timeZone: string };
export type Session = { person: Person; phone: string; clinic: Clinic };
export type SignInStep = 'welcome' | 'phone' | 'code' | 'who' | 'biometric';

const mobile: Record<Country, { prefix: string; pattern: RegExp }> = {
  IN: { prefix: '+91', pattern: /^[6-9]\d{9}$/ }, // 10 digits, starting 6–9
  AE: { prefix: '+971', pattern: /^5\d{8}$/ }, // 9 digits after the code, starting 5
};
export const dialCode = (c: Country) => mobile[c].prefix;

/** A typed mobile number as E.164, or null. Spaces, dashes, a leading 0 and a repeated country code are forgiven. */
export function toE164(country: Country, typed: string): string | null {
  const prefix = mobile[country].prefix;
  let digits = typed.replace(/[^\d+]/g, '');
  if (digits.startsWith(prefix)) digits = digits.slice(prefix.length);
  digits = digits.replace(/^\+/, '').replace(/^0/, '');
  return mobile[country].pattern.test(digits) ? `${prefix}${digits}` : null;
}

export const phoneForm = z.object({ country: z.enum(['IN', 'AE']), number: z.string() })
  .refine(v => toE164(v.country, v.number) !== null, { path: ['number'], message: 'mobile.patientSignIn.phoneInvalid' });
export const codeForm = z.object({ code: z.string().regex(/^\d{6}$/, 'mobile.signIn.codeInvalid') });
export type PhoneForm = z.infer<typeof phoneForm>;
export type CodeForm = z.infer<typeof codeForm>;

/** Shows only the last four digits of a number on screen, e.g. "+91 ••••••0101". */
export const maskPhone = (e164: string) => `${e164.startsWith(mobile.AE.prefix) ? mobile.AE.prefix : mobile.IN.prefix} ••••••${e164.slice(-4)}`;

export const LOCK_AFTER_MS = 15 * 60 * 1000;
// shortcut: same lock rule as the staff app, duplicated; both move to packages/auth-client with native tokens (MDV-P5-033).
export const shouldLock = (backgroundedAt: number | null, now: number) => backgroundedAt !== null && now - backgroundedAt >= LOCK_AFTER_MS;

/** The words for the phone's unlock (MDV-P5-059): Face ID and Touch ID are Apple names, so Android never shows them. */
export type UnlockMethod = 'faceId' | 'touchId' | 'ios' | 'android';
export function unlockMethod(os: string, has: { face: boolean; finger: boolean }): UnlockMethod {
  if (os !== 'ios') return 'android';
  return has.face ? 'faceId' : has.finger ? 'touchId' : 'ios';
}
