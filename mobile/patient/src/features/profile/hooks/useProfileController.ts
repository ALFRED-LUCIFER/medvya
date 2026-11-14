import { useI18n } from '@medvya/i18n';
import { useQuery } from '@tanstack/react-query';
import Constants from 'expo-constants';
import { errorKey } from '../../api/domain/errors';
import { useSignedIn } from '../../session/hooks/session';
import { useUnlockMethod } from '../../session/hooks/useUnlockMethod';
import { maskPhone } from '../../session/domain/session';
import { fetchFamily, fetchProfile } from '../services/profile';

export function useProfileController() {
  const { session, biometric, lock, signOut } = useSignedIn();
  const { locale } = useI18n();
  const profile = useQuery({ queryKey: ['profile', session.person.id], queryFn: () => fetchProfile(session.person.id) });
  const family = useQuery({ queryKey: ['family', session.person.id], queryFn: () => fetchFamily(session.person.id) });
  return {
    session,
    phone: maskPhone(session.phone),
    profile: profile.data ?? null,
    family: family.data ?? [],
    loading: profile.isPending,
    error: profile.error ? errorKey(profile.error) : null,
    biometric,
    unlockMethod: useUnlockMethod(),
    locale,
    version: Constants.expoConfig?.version ?? '',
    onLock: lock,
    onSignOut: signOut,
  };
}
