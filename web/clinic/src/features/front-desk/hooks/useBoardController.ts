import { useEffect, useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { groupByColumn, moveError, serverNow, type Board, type BoardCard, type BoardColumn, type LiveStatus, type MoveError } from '../domain/board';
import { ApiError } from '../../schedule/services/scheduleApi';
import { watchBoard } from '../services/boardLive';
import { checkIn, fetchBoard, monotonicNow, moveVisit } from '../services/frontDeskApi';

type Snapshot = { board: Board; fetchedAt: number };
type Move = { id: string; state: BoardColumn };

/** Wait timers move on every half minute; nothing animates, so reduced motion is unaffected. */
const tickMs = 30_000;

/**
 * P-01 reception board of the active facility (MDV-P1-028, MDV-P1-029). Check-in is two taps: "Check in" opens the identity
 * check (name and date of birth) and "Confirm arrival" checks in; the time between them is sent for the exit-gate median.
 * Changes elsewhere arrive over the board hub and trigger a refetch; after a drop the board refetches once rejoined, and
 * while not live it refreshes every 30 s, so a move is never lost or shown twice (the board is always replaced whole).
 */
export function useBoardController(facilityId: string, online: boolean) {
  const queryClient = useQueryClient();
  const key = ['front-desk', facilityId];
  const [connection, setLive] = useState<LiveStatus>('connecting');
  // Offline: nothing to connect to; back online, a fresh connection joins and refetches whatever was missed.
  const live: LiveStatus = online ? connection : 'paused';
  useEffect(() => {
    if (!online) return undefined;
    const refresh = () => void queryClient.invalidateQueries({ queryKey: ['front-desk', facilityId] });
    return watchBoard(facilityId, { onChange: refresh, onResync: refresh, onStatus: setLive });
  }, [facilityId, online, queryClient]);
  const query = useQuery({
    queryKey: key,
    queryFn: async (): Promise<Snapshot> => ({ board: await fetchBoard(facilityId), fetchedAt: Date.now() }),
    refetchInterval: live === 'live' ? false : 30_000,
    retry: (count, error) => !(error instanceof ApiError && error.status === 403) && count < 2,
  });

  const [tick, setTick] = useState(() => Date.now());
  useEffect(() => {
    const timer = setInterval(() => setTick(Date.now()), tickMs);
    return () => clearInterval(timer);
  }, []);

  const [confirming, setConfirming] = useState<{ id: string; startedAt: number } | null>(null);
  const [failure, setFailure] = useState<{ id: string; error: MoveError } | null>(null);

  const replace = (card: BoardCard) => {
    queryClient.setQueryData<Snapshot>(key, old => old && { ...old, board: { ...old.board, cards: old.board.cards.map(c => (c.id === card.id ? card : c)) } });
    void queryClient.invalidateQueries({ queryKey: key });
  };
  const fail = (id: string, e: unknown) => {
    const error = moveError(e instanceof ApiError ? e.code : undefined);
    setFailure({ id, error });
    if (error !== 'failed') void queryClient.invalidateQueries({ queryKey: key }); // it moved on elsewhere: show where it is now
  };

  const arrive = useMutation({
    mutationFn: ({ id, elapsedMs }: { id: string; elapsedMs: number }) => checkIn(id, elapsedMs),
    onSuccess: card => { setConfirming(null); replace(card); },
    onError: (e, { id }) => fail(id, e),
  });
  const move = useMutation({
    mutationFn: ({ id, state }: Move) => moveVisit(id, state),
    onSuccess: replace,
    onError: (e, { id }) => fail(id, e),
  });

  const snapshot = query.data;
  const columns = useMemo(() => groupByColumn(snapshot?.board.cards ?? []), [snapshot]);
  const forbidden = query.error instanceof ApiError && query.error.status === 403;
  const pendingId = arrive.isPending ? arrive.variables.id : move.isPending ? move.variables.id : null;

  return {
    board: snapshot?.board ?? null,
    columns,
    now: snapshot ? serverNow(snapshot.board, snapshot.fetchedAt, tick) : tick,
    loading: query.isPending && online,
    offline: !online && !snapshot,
    online,
    live,
    forbidden,
    failed: query.isError && !forbidden && !snapshot,
    confirmingId: confirming?.id ?? null,
    pendingId,
    failure,
    onRetry: () => void query.refetch(),
    onStartCheckIn: (id: string) => {
      setFailure(null);
      setConfirming({ id, startedAt: monotonicNow() });
    },
    onConfirmCheckIn: () => confirming && arrive.mutate({ id: confirming.id, elapsedMs: monotonicNow() - confirming.startedAt }),
    onCancelCheckIn: () => setConfirming(null),
    onMove: (id: string, state: BoardColumn) => {
      setFailure(null);
      move.mutate({ id, state });
    },
  };
}

export type BoardViewModel = ReturnType<typeof useBoardController>;
