import { useI18n } from '@medvya/i18n';
import { useMutation } from '@tanstack/react-query';
import { unlockWithBiometrics } from '../services/session';
import { useSignedIn } from './session';
// shortcut: duplicated in mobile/clinician; both move to packages/auth-client with native tokens (MDV-P5-033).

export function useLockController() {
  const { t } = useI18n();
  const { session, biometric, unlock, signOut } = useSignedIn();
  const attempt = useMutation({ mutationFn: () => unlockWithBiometrics(t('mobile.lock.prompt')), onSuccess: ok => { if (ok) unlock(); } });
  return {
    name: session.person.name,
    initials: session.person.initials,
    biometric,
    failed: attempt.data === false,
    busy: attempt.isPending,
    onUnlock: () => attempt.mutate(),
    // shortcut: no app PIN yet, so without Face ID the way back is the SMS code again; the PIN arrives with MDV-P5-033.
    onSignInAgain: signOut,
  };
}
