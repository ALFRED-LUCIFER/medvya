import { z } from 'zod';
import type { Country } from '@medvya/i18n';
import type { OnboardingData } from './onboarding';

/**
 * Create account steps 1 and 2 (F-03, MDV-P1-059). Nothing reaches the server before the provider's account exists: the email becomes the
 * login hint, the rest pre-fills the setup draft (MDV-P1-004) once it is created. No mobile is asked: account recovery is the provider's.
 */
export const signUpSchema = z.object({
  country: z.enum(['IN', 'AE']),
  practiceName: z.string().trim().min(1).max(200),
  city: z.string().trim().min(1).max(200),
  fullName: z.string().trim().min(1).max(200),
  email: z.email().max(254),
  role: z.enum(['doctor', 'manager']),
});
export type SignUpForm = z.infer<typeof signUpSchema>;

export const signUpStepFields = { 1: ['practiceName', 'city'], 2: ['fullName', 'email', 'role'] } as const satisfies Record<1 | 2, (keyof SignUpForm)[]>;

export const emptySignUp = (country: Country): SignUpForm => ({ country, practiceName: '', city: '', fullName: '', email: '', role: 'doctor' });

/** The answers as a draft: a doctor practises (Solo, with the licence step), a manager runs the clinic (no licence step). The city starts the address. */
export const toPrefill = (f: SignUpForm): OnboardingData => ({
  profile: { fullName: f.fullName.trim(), mode: f.role === 'doctor' ? 'Solo' : 'Clinic' },
  clinic: { name: f.practiceName.trim(), address: f.city.trim() },
});
