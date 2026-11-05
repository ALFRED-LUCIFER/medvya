import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ApiError } from '../../schedule/services/scheduleApi';
import { acceptTerms, fetchTerms } from '../services/settingsApi';

/** Terms tab: the accepted-terms status and the owner's accept action. The server decides who may accept; this follows its answer. */
export function useTermsController() {
  const queryClient = useQueryClient();
  const terms = useQuery({ queryKey: ['settings', 'terms'], queryFn: fetchTerms });
  const accept = useMutation({ mutationFn: acceptTerms, onSuccess: data => queryClient.setQueryData(['settings', 'terms'], data) });
  return {
    terms: terms.data ?? null,
    loading: terms.isPending,
    loadFailed: terms.isError,
    accepting: accept.isPending,
    notOwner: accept.error instanceof ApiError && accept.error.status === 403,
    acceptFailed: accept.isError && !(accept.error instanceof ApiError && accept.error.status === 403),
    onAccept: () => accept.mutate(),
  };
}
