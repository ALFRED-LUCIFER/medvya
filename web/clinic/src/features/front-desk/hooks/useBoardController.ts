import { useEffect, useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { workload } from '../domain/overview';
import { canUndo, groupByColumn, isBoardColumn, moveError, serverNow, undoWindowMs, type Board, type BoardCard, type BoardColumn, type EndKind, type LiveStatus, type MoveError, type Notice } from '../domain/board';
import { cancelSchema, emptyCancel, type CancelForm } from '../../schedule/domain/changes';
import { ApiError, cancelAppointment } from '../../schedule/services/scheduleApi';
import { sessionQueryKey } from '../../session/hooks/useSessionController';
import { watchBoard } from '../services/boardLive';
import { checkIn, fetchBoard, fetchFailedVisits, markLeft, monotonicNow, moveVisit, undoMove } from '../services/frontDeskApi';

type Snapshot = { board: Board; fetchedAt: number };
type Move = { id: string; state: BoardColumn };
type Ending = { card: BoardCard; kind: EndKind | null };

/** Wait timers move on every half minute; nothing animates, so reduced motion is unaffected. */
const tickMs = 30_000;

/**
 * P-01 reception board of the active facility (MDV-P1-028, MDV-P1-029). Check-in is two taps: "Check in" opens the identity
 * check (name and date of birth) and "Confirm arrival" checks in; the time between them is sent for the exit-gate median.
 * Changes elsewhere arrive over the board hub and trigger a refetch; after a drop the board refetches once rejoined, and
 * while not live it refreshes every 30 s, so a move is never lost or shown twice (the board is always replaced whole).
 * After a move the status line offers Undo for 30 seconds or until the next change here; a waiting card's menu ends the visit as
 * left without being seen or cancelled, with a reason (MDV-P1-100). The server decides each of them again.
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
  });

  // Marks for visits whose message failed; refreshed with the board. A refusal (403) or any failure just means no marks.
  const failedQuery = useQuery({ queryKey: ['front-desk-failed', facilityId], queryFn: () => fetchFailedVisits(facilityId), refetchInterval: 30_000, retry: false, enabled: online });
  const failedVisitIds = useMemo(() => new Set(failedQuery.data ?? []), [failedQuery.data]);

  const [tick, setTick] = useState(() => Date.now());
  useEffect(() => {
    const timer = setInterval(() => setTick(Date.now()), tickMs);
    return () => clearInterval(timer);
  }, []);

  const [confirming, setConfirming] = useState<{ id: string; startedAt: number } | null>(null);
  const [failure, setFailure] = useState<{ id: string; error: MoveError } | null>(null);
  const [notice, setNotice] = useState<Notice | null>(null);
  useEffect(() => {
    if (!notice?.undo) return undefined;
    const timer = setTimeout(() => setNotice(n => n && { ...n, undo: false }), undoWindowMs);
    return () => clearTimeout(timer);
  }, [notice]);
  const [ending, setEnding] = useState<Ending | null>(null);
  const [endFailed, setEndFailed] = useState(false);
  const endForm = useForm<CancelForm>({ resolver: zodResolver(cancelSchema), defaultValues: emptyCancel });
  const closeEnd = () => {
    setEnding(null);
    setEndFailed(false);
    endForm.reset(emptyCancel);
  };

  const replace = (card: BoardCard) => {
    queryClient.setQueryData<Snapshot>(key, old => old && { ...old, board: { ...old.board, cards: old.board.cards.map(c => (c.id === card.id ? card : c)) } });
    void queryClient.invalidateQueries({ queryKey: key });
  };
  const fail = (id: string, e: unknown) => {
    const error = moveError(e instanceof ApiError ? e.code : undefined);
    setFailure({ id, error });
    if (error !== 'failed') void queryClient.invalidateQueries({ queryKey: key }); // it moved on elsewhere: show where it is now
    // Or the owner turned a visit step on or off since this session loaded: refetch the flags so the columns and buttons match the server.
    if (error === 'invalid_transition') void queryClient.invalidateQueries({ queryKey: sessionQueryKey });
  };

  const arrive = useMutation({
    mutationFn: ({ id, elapsedMs }: { id: string; elapsedMs: number }) => checkIn(id, elapsedMs),
    onSuccess: card => { setConfirming(null); setNotice(null); replace(card); },
    onError: (e, { id }) => fail(id, e),
  });
  const move = useMutation({
    mutationFn: ({ id, state }: Move) => moveVisit(id, state),
    onMutate: ({ id }) => query.data?.board.cards.find(c => c.id === id)?.state, // where it was, to know whether the move can be undone
    onSuccess: (card, { state }, from) => {
      replace(card);
      setNotice({ kind: 'moved', id: card.id, name: card.patientName, state, undo: from !== undefined && isBoardColumn(from) && canUndo(from, state) });
    },
    onError: (e, { id }) => fail(id, e),
  });
  const undo = useMutation({
    mutationFn: (n: Notice) => undoMove(n.id, n.state),
    onSuccess: (card, n) => {
      replace(card);
      setNotice({ ...n, kind: 'undone', state: card.state as BoardColumn, undo: false });
    },
    onError: (e, n) => { setNotice(null); fail(n.id, e); },
  });
  const end = useMutation({
    mutationFn: ({ ending: { card, kind }, form }: { ending: Ending; form: CancelForm }) => (kind === 'left' ? markLeft : cancelAppointment)(card.id, form.reason, form.note),
    onSuccess: (_, { ending: { card, kind } }) => {
      setNotice({ kind: kind!, id: card.id, name: card.patientName, state: card.state as BoardColumn, undo: false });
      closeEnd();
      void queryClient.invalidateQueries({ queryKey: key });
    },
    onError: (e, { ending: { card } }) => {
      // Moved on elsewhere (a 409 with a code): close and say so on the card. Anything else keeps the form and what was typed.
      if (moveError(e instanceof ApiError ? e.code : undefined) === 'failed') return setEndFailed(true);
      closeEnd();
      fail(card.id, e);
    },
  });

  const snapshot = query.data;
  const columns = useMemo(() => groupByColumn(snapshot?.board.cards ?? []), [snapshot]);
  const now = snapshot ? serverNow(snapshot.board, snapshot.fetchedAt, tick) : tick;
  const forbidden = query.error instanceof ApiError && query.error.status === 403;
  const pendingId = arrive.isPending ? arrive.variables.id : move.isPending ? move.variables.id : null;

  return {
    board: snapshot?.board ?? null,
    columns,
    failedVisitIds,
    now,
    /** Doctor workload panel (MDV-P1-030): per doctor, how many wait and how late they run. */
    workload: snapshot ? workload(snapshot.board.cards, now) : [],
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
    /** What this desk last did, for the status line; `undo` while the move can still be undone. */
    notice,
    undoing: undo.isPending,
    onUndo: () => {
      setFailure(null);
      if (notice?.undo) undo.mutate(notice);
    },
    /** The card whose visit is being ended from its menu; kind null while the desk still chooses between left and cancel. */
    ending,
    endForm,
    endWorking: end.isPending,
    endFailed,
    onStartEnd: (card: BoardCard, kind: EndKind | null) => {
      setFailure(null);
      setEnding({ card, kind });
    },
    onEndKind: (kind: EndKind) => setEnding(e => e && { ...e, kind }),
    onEndReason: (reason: CancelForm['reason']) => endForm.setValue('reason', reason, { shouldValidate: endForm.formState.isSubmitted }),
    onConfirmEnd: () => {
      setEndFailed(false);
      void endForm.handleSubmit(form => ending?.kind && end.mutate({ ending, form }))();
    },
    onCancelEnd: closeEnd,
  };
}

export type BoardViewModel = ReturnType<typeof useBoardController>;
