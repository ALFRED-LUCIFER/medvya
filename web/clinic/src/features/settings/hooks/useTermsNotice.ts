import { useQuery } from '@tanstack/react-query';
import { fetchTerms } from '../services/settingsApi';

/** True for someone who administers the practice while the terms are still not accepted; shares the Terms tab's cached query. Others never ask (the endpoint is 403 for them). */
export function useTermsNotice(canAdminister: boolean) {
  const terms = useQuery({ queryKey: ['settings', 'terms'], queryFn: fetchTerms, enabled: canAdminister, retry: false });
  return !!terms.data && terms.data.required && !terms.data.accepted;
}
