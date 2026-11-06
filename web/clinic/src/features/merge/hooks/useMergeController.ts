import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { loadState, patientDataGcTime } from '../../patients/hooks/usePatientDirectoryController';
import { usePatientSearch } from '../../patients/hooks/usePatientSearch';
import { ApiError } from '../../patients/services/patientsApi';
import { refusalOf, type MergeAction } from '../domain/merge';
import { decideMerge, fetchMergeHistory, fetchMergeQueue, fetchMergeReview, requestMerge } from '../services/mergeApi';

// Each fetch is an audited read: reuse it until a decision changes it. Memory only.
const options = { gcTime: patientDataGcTime, staleTime: Infinity };

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

/** "Request merge review" on a record: pick the other record with front-desk search, then ask. `onRequested` gets the new request's id. */
export function useMergeRequest(patientId: string, onRequested: (requestId: string) => void) {
  const client = useQueryClient();
  const [other, setOther] = useState('');
  const search = usePatientSearch(setOther);
  const request = useMutation({
    mutationFn: () => requestMerge(patientId, other),
    onSuccess: async id => {
      await client.invalidateQueries({ queryKey: ['merge'] });
      onRequested(id);
    },
  });
  return {
    search: search.view, chosen: other, busy: request.isPending,
    refusal: !request.isError ? null : request.error instanceof ApiError ? refusalOf(request.error.code, request.error.status) : refusalOf(undefined, 0),
    onRequest: () => request.mutate(),
  };
}
