import { z } from 'zod';

export type Role = 'doctor' | 'frontDesk';
export type Staff = { id: string; name: string; initials: string; email: string; role: Role };
export type Facility = { id: string; name: string; city: string; opens: string; closes: string; timeZone: string };
export type Session = { staff: Staff; facility: Facility };
export type SignInStep = 'email' | 'code' | 'facility' | 'biometric';

// Messages are i18n keys; the component translates them.
export const emailForm = z.object({ email: z.string().trim().toLowerCase().pipe(z.email('mobile.signIn.emailInvalid')) });
export const codeForm = z.object({ code: z.string().regex(/^\d{6}$/, 'mobile.signIn.codeInvalid') });
export type EmailForm = z.infer<typeof emailForm>;
export type CodeForm = z.infer<typeof codeForm>;

export type TabKey = 'today' | 'patients' | 'consult' | 'inbox';
/** Front desk never gets clinical tabs. The UI hides them; the API is what refuses (403). Five tabs maximum. */
export const tabsFor = (role: Role): TabKey[] => (role === 'doctor' ? ['today', 'patients', 'consult', 'inbox'] : ['today', 'patients', 'inbox']);

export const LOCK_AFTER_MS = 15 * 60 * 1000;
/** The app locks once it has been in the background for 15 minutes (MDV-P5-034). */
export const shouldLock = (backgroundedAt: number | null, now: number) => backgroundedAt !== null && now - backgroundedAt >= LOCK_AFTER_MS;
