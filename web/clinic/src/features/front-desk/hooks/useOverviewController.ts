import { useEffect, useState } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { moveError, type LiveStatus, type MoveError } from '../domain/board';
import { reassignSchema, type ReassignInput } from '../domain/overview';
import { ApiError } from '../../schedule/services/scheduleApi';
import { watchBoard } from '../services/boardLive';
import { fetchOverview, reassignVisit } from '../services/frontDeskApi';

/**
 * D-04 clinic overview (MDV-P1-030): each doctor's load, delay and next patients, and the shared waiting room, live like the
 * board. The queue can move a waiting patient to another doctor (doctor and a listed reason, then confirm); the server checks
 * the move is still possible and audits it.
 */
export function useOverviewController(facilityId: string, online: boolean, userId: string) {
  const queryClient = useQueryClient();
  const key = ['front-desk-overview', facilityId];
  const [connection, setLive] = useState<LiveStatus>('connecting');
  const live: LiveStatus = online ? connection : 'paused';
  useEffect(() => {
    if (!online) return undefined;
    const refresh = () => void queryClient.invalidateQueries({ queryKey: ['front-desk-overview', facilityId] });
    return watchBoard(facilityId, { onChange: refresh, onResync: refresh, onStatus: setLive });
  }, [facilityId, online, queryClient]);
  const query = useQuery({
    queryKey: key,
    queryFn: () => fetchOverview(facilityId),
    refetchInterval: live === 'live' ? false : 30_000,
    retry: (count, error) => !(error instanceof ApiError && error.status === 403) && count < 2,
  });

  const [movingId, setMovingId] = useState<string | null>(null);
  const [failure, setFailure] = useState<{ id: string; error: MoveError } | null>(null);
  const form = useForm<ReassignInput>({ resolver: zodResolver(reassignSchema), defaultValues: { calendarId: '', reason: 'DoctorRunningLate' } });
  const reassign = useMutation({
    mutationFn: ({ id, values }: { id: string; values: ReassignInput }) => reassignVisit(id, values.calendarId, values.reason),
    onSuccess: () => { setMovingId(null); void queryClient.invalidateQueries({ queryKey: key }); },
    onError: (e, { id }) => {
      setFailure({ id, error: moveError(e instanceof ApiError ? e.code : undefined) });
      void queryClient.invalidateQueries({ queryKey: key }); // it may have moved on elsewhere
    },
  });

  const forbidden = query.error instanceof ApiError && query.error.status === 403;
  const overview = query.data ?? null;
  return {
    overview,
    /** The signed-in doctor's own row (their next patients first); null for everyone else. */
    mine: overview?.doctors.find(d => d.calendarId === userId) ?? null,
    loading: query.isPending && online,
    offline: !online && !overview,
    online,
    live,
    forbidden,
    failed: query.isError && !forbidden && !overview,
    onRetry: () => void query.refetch(),
    form,
    movingId,
    moving: reassign.isPending,
    failure,
    onStartMove: (id: string) => {
      setFailure(null);
      form.reset({ calendarId: '', reason: 'DoctorRunningLate' });
      setMovingId(id);
    },
    onCancelMove: () => setMovingId(null),
    onConfirmMove: form.handleSubmit(values => movingId && reassign.mutate({ id: movingId, values })),
  };
}

export type OverviewViewModel = ReturnType<typeof useOverviewController>;
