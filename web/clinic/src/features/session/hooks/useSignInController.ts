import { zodResolver } from '@hookform/resolvers/zod';
import { useForm } from 'react-hook-form';
import { signInSchema, type SignInForm } from '../domain/signIn';
import { signInFailed, startSignIn } from '../services/sessionApi';

/**
 * F-03 sign-in start (MDV-P1-059). The email only becomes the provider's login hint; codes, passkeys and MFA run on the provider's pages.
 * shortcut: no "This is a shared computer" switch: the server has no shared-device session policy (only the tenant idle timeout and the
 * 8 h cap), and a switch that changes nothing would mislead; add it with that server policy.
 */
export function useSignInController() {
  const form = useForm<SignInForm>({ resolver: zodResolver(signInSchema), defaultValues: { email: '' } });
  return {
    form,
    failed: signInFailed(),
    submit: form.handleSubmit(v => startSignIn(v.email.trim())),
    // Passkey and organisation sign-in are chosen on the provider's page; both start the same sign-in without a hint.
    startWithoutEmail: () => startSignIn(),
  };
}
