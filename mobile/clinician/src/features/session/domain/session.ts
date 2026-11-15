import { z } from 'zod';

export type Role = 'doctor' | 'frontDesk';
/** `email` is empty when signed in through the server, whose context names the person but not their email. */
export type Staff = { id: string; name: string; initials: string; email: string; role: Role };
/**
 * `id` is the facility's id. From the server, `membershipId` is what the facility choice sends, `city` is the practice's
 * name and there are no opening hours; the synthetic cast has hours and no membership.
 */
export type Facility = { id: string; name: string; city: string; opens?: string; closes?: string; timeZone: string; membershipId?: string };
export type Session = { staff: Staff; facility: Facility };
/** `browser` is sign-in through the server (system browser); `email` and `code` are the Development cast's stand-in. */
export type SignInStep = 'browser' | 'email' | 'code' | 'facility' | 'pin' | 'biometric';

/**
 * The server's role (RoleMatrix) as the app's tab set. Only doctors get the clinical tabs; a nurse or admin gets front
 * desk's. The tabs are convenience: the API refuses what a role may not do (MDV-P5-034 check 2).
 */
export const roleOf = (serverRole: string): Role => (serverRole === 'SoloDoctor' || serverRole === 'HospitalDoctor' ? 'doctor' : 'frontDesk');

/** Up to two initials from the name's whole words, skipping titles and abbreviations ("Dr.", "A.") and bracketed notes. */
export function initialsOf(name: string) {
  const words = name.split(/\s+/).filter(w => /^[\p{L}\p{M}]+$/u.test(w));
  const picked = words.length > 1 ? [words[0]!, words[words.length - 1]!] : words;
  return picked.map(w => w[0]!.toLocaleUpperCase()).join('') || name.trim().slice(0, 1);
}

// Messages are i18n keys; the component translates them.
export const emailForm = z.object({ email: z.string().trim().toLowerCase().pipe(z.email('mobile.signIn.emailInvalid')) });
export const codeForm = z.object({ code: z.string().regex(/^\d{6}$/, 'mobile.signIn.codeInvalid') });
const pin = z.string().regex(/^\d{6}$/, 'mobile.pin.invalid');
export const pinForm = z.object({ pin });
export const newPinForm = z.object({ pin, confirm: z.string() }).refine(v => v.pin === v.confirm, { path: ['confirm'], message: 'mobile.pin.mismatch' });
export type EmailForm = z.infer<typeof emailForm>;
export type CodeForm = z.infer<typeof codeForm>;
export type PinForm = z.infer<typeof pinForm>;
export type NewPinForm = z.infer<typeof newPinForm>;

/** Wrong PINs allowed before the app signs out and wipes the phone (ADR-011 §3, "a failed PIN limit"). */
export const PIN_TRIES = 5;
export const pinTriesLeft = (wrong: number) => Math.max(PIN_TRIES - wrong, 0);
/** The app PIN is the way in when the phone's face or fingerprint unlock is off or has just failed (ADR-011 §2). */
export const offersPin = (biometric: boolean, biometricFailed: boolean) => !biometric || biometricFailed;

export type TabKey = 'today' | 'patients' | 'consult' | 'inbox';
/** Front desk never gets clinical tabs. The UI hides them; the API is what refuses (403). Five tabs maximum. */
export const tabsFor = (role: Role): TabKey[] => (role === 'doctor' ? ['today', 'patients', 'consult', 'inbox'] : ['today', 'patients', 'inbox']);

export const LOCK_AFTER_MS = 15 * 60 * 1000;
/** The app locks once it has been in the background for 15 minutes (MDV-P5-034). */
export const shouldLock = (backgroundedAt: number | null, now: number) => backgroundedAt !== null && now - backgroundedAt >= LOCK_AFTER_MS;

/** The words for the phone's unlock (MDV-P5-059): Face ID and Touch ID are Apple names, so Android never shows them. */
export type UnlockMethod = 'faceId' | 'touchId' | 'ios' | 'android';
export function unlockMethod(os: string, has: { face: boolean; finger: boolean }): UnlockMethod {
  if (os !== 'ios') return 'android';
  return has.face ? 'faceId' : has.finger ? 'touchId' : 'ios';
}
