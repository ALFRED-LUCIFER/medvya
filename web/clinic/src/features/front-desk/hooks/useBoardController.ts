import { useEffect, useMemo, useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { workload } from '../domain/overview';
import { canUndo, echoMs, groupByColumn, isBoardColumn, isOthersChange, moveError, serverNow, undoWindowMs, withUpdated, type Board, type BoardCard, type BoardChange, type BoardColumn, type EndKind, type LiveStatus, type MoveError, type Notice, type UpdatedMarks } from '../domain/board';
import { cancelSchema, emptyCancel, type CancelForm } from '../../schedule/domain/changes';
import { ApiError, cancelAppointment } from '../../schedule/services/scheduleApi';
import { sessionQueryKey } from '../../session/hooks/useSessionController';
import { watchBoard } from '../services/boardLive';
import { checkIn, fetchBoard, fetchFailedVisits, markLeft, monotonicNow, moveVisit, undoMove } from '../services/frontDeskApi';

/** marks: cards that say Updated; heard: the visits this fetch is the first to show changed elsewhere (announced once). */
type Snapshot = { board: Board; fetchedAt: number; marks: UpdatedMarks; heard: string[] };
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
 * A card the live connection reports changed by someone else says Updated for 5 seconds (MDV-P2-056); a resync after a drop marks
 * nothing, since those changes were not delivered live, and this desk's own changes coming back are not marked.
 */
export function useBoardController(facilityId: string, online: boolean) {
  const queryClient = useQueryClient();
  const key = ['front-desk', facilityId];
  const [connection, setLive] = useState<LiveStatus>('connecting');
  // Offline: nothing to connect to; back online, a fresh connection joins and refetches whatever was missed.
  const live: LiveStatus = online ? connection : 'paused';
  // Visits the live connection reported changed elsewhere since the last fetch, and this desk's own changes (id → until when their echo is ignored).
  const heard = useRef(new Set<string>());
  const mine = useRef(new Map<string, number>());
  useEffect(() => {
    if (!online) return undefined;
    const refresh = () => void queryClient.invalidateQueries({ queryKey: ['front-desk', facilityId] });
    const onChange = (change?: BoardChange) => {
      if (typeof change?.appointmentId === 'string' && isOthersChange(mine.current, change.appointmentId, Date.now())) heard.current.add(change.appointmentId);
      refresh();
    };
    return watchBoard(facilityId, { onChange, onResync: refresh, onStatus: setLive });
  }, [facilityId, online, queryClient]);
  const query = useQuery({
    queryKey: key,
    queryFn: async (): Promise<Snapshot> => {
      const changed = [...heard.current];
      const board = await fetchBoard(facilityId);
      changed.forEach(id => heard.current.delete(id)); // a failed fetch keeps them for the next one
      const fetchedAt = Date.now();
      return { board, fetchedAt, marks: withUpdated(queryClient.getQueryData<Snapshot>(key)?.marks ?? {}, changed, fetchedAt), heard: changed };
    },
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
  // The next Updated mark to end takes its tag away on time.
  const marks = query.data?.marks;
  useEffect(() => {
    const next = Math.min(...Object.values(marks ?? {}).filter(until => until > Date.now()));
    if (!Number.isFinite(next)) return undefined;
    const timer = setTimeout(() => setTick(Date.now()), next - Date.now());
    return () => clearTimeout(timer);
  }, [marks, tick]);
  // This desk's own change: its echo over the hub is not someone else's (in flight, then echoMs after it settles).
  const own = (id: string) => void mine.current.set(id, Number.POSITIVE_INFINITY);
  const settled = (id: string) => void mine.current.set(id, Date.now() + echoMs);

  const [confirming, setConfirming] = useState<{ id: string; startedAt: number } | null>(null);
  const [failure, setFailure] = useState<{ id: string; error: MoveError } | null>(null);
  const [notice, setNotice] = useState<Notice | null>(null);
  useEffect(() => {
    if (!notice?.undo) return undefined;
    const timer = setTimeout(() => setNotice(n => n && { ...n, undo: false }), undoWindowMs);
    return () => clearTimeout(timer);
  }, [notice]);
  const [phoneColumn, setPhoneColumn] = useState<BoardColumn | null>(null);
  const [listFilter, setListFilter] = useState<BoardColumn | null>(null);
  const [ending, setEnding] = useState<Ending | null>(null);
  const [endFailed, setEndFailed] = useState(false);
  const endForm = useForm<CancelForm>({ resolver: zodResolver(cancelSchema), defaultValues: emptyCancel });
  const closeEnd = () => {
    setEnding(null);
    setEndFailed(false);
    endForm.reset(emptyCancel);
  };

  // A moved card keeps its queue place until the refetch brings the server's (MDV-P1-103).
  const replace = (card: BoardCard) => {
    queryClient.setQueryData<Snapshot>(key, old => old && { ...old, board: { ...old.board, cards: old.board.cards.map(c => (c.id === card.id ? { ...card, queuePlace: c.queuePlace } : c)) } });
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
    onMutate: ({ id }) => own(id),
    onSettled: (_, __, { id }) => settled(id),
    onSuccess: card => { setConfirming(null); setNotice(null); replace(card); },
    onError: (e, { id }) => fail(id, e),
  });
  const move = useMutation({
    mutationFn: ({ id, state }: Move) => moveVisit(id, state),
    onMutate: ({ id }) => {
      own(id);
      return query.data?.board.cards.find(c => c.id === id)?.state; // where it was, to know whether the move can be undone
    },
    onSettled: (_, __, { id }) => settled(id),
    onSuccess: (card, { state }, from) => {
      replace(card);
      setNotice({ kind: 'moved', id: card.id, name: card.patientName, state, undo: from !== undefined && isBoardColumn(from) && canUndo(from, state) });
    },
    onError: (e, { id }) => fail(id, e),
  });
  const undo = useMutation({
    mutationFn: (n: Notice) => undoMove(n.id, n.state),
    onMutate: n => own(n.id),
    onSettled: (_, __, n) => settled(n.id),
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
  const updated = useMemo(() => new Set(Object.keys(marks ?? {}).filter(id => marks![id]! > tick)), [marks, tick]);

  return {
    board: snapshot?.board ?? null,
    columns,
    failedVisitIds,
    now,
    /** Cards changed elsewhere in the last 5 seconds, as the live connection reported (MDV-P2-056). */
    updated,
    /** The cards this fetch is the first to show changed elsewhere, still marked: said once in the status line's live region. */
    updatedCards: snapshot ? snapshot.board.cards.filter(c => updated.has(c.id) && snapshot.heard.includes(c.id) && isBoardColumn(c.state)) : [],
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
    /** Under 768 px the board is one list; the status it shows (null: the first with visits). */
    phoneColumn,
    onPhoneColumn: setPhoneColumn,
    /** The status the List view shows alone (MDV-P2-052); pressing it again shows every status. */
    listFilter,
    onListFilter: (column: BoardColumn) => setListFilter(f => (f === column ? null : column)),
  };
}

export type BoardViewModel = ReturnType<typeof useBoardController>;
