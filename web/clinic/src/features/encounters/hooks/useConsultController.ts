import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ApiError } from '../../schedule/services/scheduleApi';
import { refusalOf } from '../../clinical-access/domain/clinicalAccess';
import { endEncounter, fetchEncounter } from '../services/encountersApi';

/** One consultation (MDV-P2-001): its identity banner, and ending it (the visit moves on to Billing on the board). */
export function useConsultController(encounterId: string) {
  const queryClient = useQueryClient();
  const key = ['encounter', encounterId];
  const query = useQuery({ queryKey: key, queryFn: () => fetchEncounter(encounterId), retry: false });
  const end = useMutation({ mutationFn: () => endEncounter(encounterId), onSuccess: () => void queryClient.invalidateQueries({ queryKey: key }) });
  const error = query.error instanceof ApiError ? query.error : null;
  return {
    banner: query.data ?? null,
    loading: query.isPending,
    notFound: error?.status === 404,
    refusal: error?.status === 403 ? (refusalOf(error.body) ?? { canRequest: false, requestPending: false }) : null,
    failed: query.isError,
    ending: end.isPending,
    endFailed: end.isError,
    onEnd: () => end.mutate(),
  };
}
