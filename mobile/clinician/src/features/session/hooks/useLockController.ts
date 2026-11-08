import { useI18n } from '@medvya/i18n';
import { useMutation } from '@tanstack/react-query';
import { unlockWithBiometrics } from '../services/session';
import { useSignedIn } from './session';
// shortcut: duplicated in mobile/patient; both move to packages/auth-client with native tokens (MDV-P5-033).

/** The lock screen shows nothing of the record: just who is signed in and how to unlock. */
export function useLockController() {
  const { t } = useI18n();
  const { session, biometric, unlock, signOut } = useSignedIn();
  const attempt = useMutation({ mutationFn: () => unlockWithBiometrics(t('mobile.lock.prompt')), onSuccess: ok => { if (ok) unlock(); } });
  return {
    name: session.staff.name,
    initials: session.staff.initials,
    biometric,
    failed: attempt.data === false,
    busy: attempt.isPending,
    onUnlock: () => attempt.mutate(),
    // shortcut: no app PIN yet, so without Face ID the way back is a fresh sign-in; the PIN fallback comes with MDV-P5-033's device key.
    onSignInAgain: signOut,
  };
}
