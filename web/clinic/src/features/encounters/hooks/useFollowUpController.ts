import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ApiError } from '../../schedule/services/scheduleApi';
import { followUpProblemOf, nextPeriod, type FollowUp, type FollowUpPeriod } from '../domain/followUp';
import { fetchFollowUp, saveFollowUp } from '../services/encountersApi';

/** The plan's follow-up (MDV-P2-043): one choice the front desk sees on its waitlist as a booking to make. The server decides who may set it. */
export function useFollowUpController(encounterId: string) {
  const queryClient = useQueryClient();
  const key = ['follow-up', encounterId];
  const query = useQuery({ queryKey: key, queryFn: () => fetchFollowUp(encounterId), retry: false });
  const save = useMutation({ mutationFn: (period: FollowUpPeriod | null) => saveFollowUp(encounterId, period), onSuccess: (f: FollowUp) => queryClient.setQueryData(key, f) });
  const error = query.error instanceof ApiError ? query.error : null;
  return {
    followUp: query.data ?? null,
    loading: query.isPending,
    hidden: error?.status === 403,
    failed: query.isError && error?.status !== 403,
    saving: save.isPending,
    problem: save.error ? followUpProblemOf(save.error instanceof ApiError ? save.error.code : undefined) : null,
    onPick: (period: FollowUpPeriod) => save.mutate(nextPeriod(query.data?.period ?? null, period)),
  };
}
