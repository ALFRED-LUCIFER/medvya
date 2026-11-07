import { z } from 'zod';

/** The sign-in start screen (F-03): only the work email, passed to the provider as a hint. The provider checks the account. */
export const signInSchema = z.object({ email: z.email().max(254) });
export type SignInForm = z.infer<typeof signInSchema>;
