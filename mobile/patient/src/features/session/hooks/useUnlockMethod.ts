import { useI18n } from '@medvya/i18n';
import { useQuery } from '@tanstack/react-query';
import { phoneUnlockMethod, platformUnlockMethod } from '../services/session';

/** The unlock words for this phone, for example "Face ID" or "fingerprint or face unlock". */
export function useUnlockMethod() {
  const { t } = useI18n();
  const method = useQuery({ queryKey: ['unlock-method'], queryFn: phoneUnlockMethod, staleTime: Infinity });
  return t(`mobile.unlock.${method.data ?? platformUnlockMethod}`);
}
