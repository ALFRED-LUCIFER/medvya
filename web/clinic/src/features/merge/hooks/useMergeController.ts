import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { loadState, noRetryOnAnswers, patientDataGcTime } from '../../patients/hooks/usePatientDirectoryController';
import { ApiError } from '../../patients/services/patientsApi';
import { refusalOf, type MergeAction } from '../domain/merge';
import { decideMerge, fetchMergeHistory, fetchMergeQueue, fetchMergeReview } from '../services/mergeApi';

// Each fetch is an audited read: reuse it until a decision changes it. Memory only.
const options = { gcTime: patientDataGcTime, staleTime: Infinity, retry: noRetryOnAnswers };

/** Merge review (P-03, MDV-P1-017): the open queue and, with a request id, the side-by-side comparison. */
export function useMergeController(requestId: string | null) {
  const client = useQueryClient();
  const queue = useQuery({ queryKey: ['merge', 'queue'], queryFn: fetchMergeQueue, ...options });
  const review = useQuery({ queryKey: ['merge', 'review', requestId], queryFn: () => fetchMergeReview(requestId!), enabled: !!requestId, ...options });
  const decide = useMutation({
    mutationFn: (action: MergeAction) => decideMerge(requestId!, action),
    // A decision changes the queue, the comparison and the records themselves.
    onSuccess: () => Promise.all([client.invalidateQueries({ queryKey: ['merge'] }), client.invalidateQueries({ queryKey: ['patients'] })]),
  });
  return {
    queue: queue.data ?? [], queueState: loadState(queue),
    review: requestId ? review.data ?? null : null, reviewState: requestId ? loadState(review) : null,
    busy: decide.isPending,
    refusal: !decide.isError ? null : decide.error instanceof ApiError ? refusalOf(decide.error.code, decide.error.status) : refusalOf(undefined, 0),
    onDecide: (action: MergeAction) => decide.mutate(action),
    onRetry: () => void queue.refetch(),
  };
}

/** The merges a record took part in, for its history section. A failed load shows no section rather than failing the record. */
export function useMergeHistory(patientId: string) {
  const history = useQuery({ queryKey: ['merge', 'history', patientId], queryFn: () => fetchMergeHistory(patientId), ...options });
  return history.data ?? [];
}
