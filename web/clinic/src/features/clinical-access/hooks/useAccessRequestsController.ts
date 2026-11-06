import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ApiError } from '../../schedule/services/scheduleApi';
import type { DecideError } from '../domain/clinicalAccess';
import { declineAccess, fetchAccessQueue, grantAccess } from '../services/clinicalAccessApi';

const key = ['access-requests'];
type Decision = { id: string; grant: boolean };

/**
 * The owner's queue of access requests (MDV-P2-024): grant each for one of the listed durations (the shortest by default) or decline.
 * The server checks who may decide and refuses the requester's own request; a request someone else already decided refreshes the list.
 */
export function useAccessRequestsController() {
  const queryClient = useQueryClient();
  const query = useQuery({ queryKey: key, queryFn: fetchAccessQueue });
  const [chosen, setChosen] = useState<Record<string, number>>({});
  const shortest = query.data?.grantDays[0] ?? 1;
  const decide = useMutation({
    mutationFn: ({ id, grant }: Decision) => (grant ? grantAccess(id, chosen[id] ?? shortest) : declineAccess(id)),
    onSettled: () => void queryClient.invalidateQueries({ queryKey: key }),
  });
  const code = decide.error instanceof ApiError ? decide.error.code : undefined;
  const decideError: DecideError | null = !decide.isError ? null : code === 'own_request' || code === 'not_pending' ? code : 'failed';
  return {
    requests: query.data?.requests ?? [],
    grantDays: query.data?.grantDays ?? [],
    loading: query.isPending,
    failed: query.isError,
    daysFor: (id: string) => chosen[id] ?? shortest,
    onDays: (id: string, days: number) => setChosen(c => ({ ...c, [id]: days })),
    decidingId: decide.isPending ? decide.variables.id : null,
    decideError,
    onGrant: (id: string) => decide.mutate({ id, grant: true }),
    onDecline: (id: string) => decide.mutate({ id, grant: false }),
  };
}
