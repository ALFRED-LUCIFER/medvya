import { z } from 'zod';

/** The sign-in start screen (F-03): only the work email, passed to the provider as a hint. The provider checks the account. */
export const signInSchema = z.object({ email: z.email().max(254) });
export type SignInForm = z.infer<typeof signInSchema>;

/** Why a sign-in came back refused: no second factor (the person can add one, #912), or any other reason (kept server-side). */
export type SignInFailure = 'mfa' | 'failed';

/** Where a person adds a second factor to their Microsoft account. */
export const securityInfoUrl = 'https://aka.ms/mysecurityinfo';
