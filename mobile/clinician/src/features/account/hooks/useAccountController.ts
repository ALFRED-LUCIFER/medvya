import { useI18n } from '@medvya/i18n';
import Constants from 'expo-constants';
import { useSignedIn } from '../../session/hooks/session';
import { useUnlockMethod } from '../../session/hooks/useUnlockMethod';

export function useAccountController() {
  const { session, biometric, lock, signOut } = useSignedIn();
  const { locale } = useI18n();
  return {
    staff: session.staff,
    facility: session.facility,
    biometric,
    unlockMethod: useUnlockMethod(),
    locale,
    version: Constants.expoConfig?.version ?? '',
    onLock: lock,
    onSignOut: signOut,
  };
}
