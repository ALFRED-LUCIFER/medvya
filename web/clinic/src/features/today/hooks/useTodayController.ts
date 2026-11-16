import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useBoardController } from '../../front-desk/hooks/useBoardController';
import { minutesSince } from '../../front-desk/domain/board';
import { elsewhereLines, escalatedVisits, MAX_FLAGS, nextPatient, patientIdsOf, todayVisits, type Scope } from '../domain/today';
import { fetchAllergyFlags, fetchElsewhere, fetchEscalations, fetchPendingNotes } from '../services/todayApi';
import { fetchPendingPrescriptions } from '../../prescription/services/prescriptionApi';
import { ApiError } from '../../api/services/apiClient';

const noLocations: { id: string; facilityId: string; facilityName: string }[] = [];

/**
 * D-01 Doctor Today (MDV-P2-002): the board's data (same audited endpoint, live updates and server clock), narrowed to the doctor's own
 * visits, or the clinic's when toggled. The server decides who may see the board; nothing here grants anything.
 */
export function useTodayController(facilityId: string, timeZone: string, online: boolean, userId: string, otherLocations: { id: string; facilityId: string; facilityName: string }[] = noLocations) {
  const board = useBoardController(facilityId, online);
  const [scope, setScope] = useState<Scope>('mine');
  const visits = useMemo(() => todayVisits(board.board?.cards ?? [], scope, userId), [board.board, scope, userId]);
  // Only for patients the clinician may treat (the server leaves the others out); a failed lookup shows no flag, never a guess.
  const ids = useMemo(() => patientIdsOf(visits), [visits]);
  const flags = useQuery({
    queryKey: ['today-allergy-flags', facilityId, ids], enabled: online && ids.length > 0, retry: false, refetchOnWindowFocus: false,
    queryFn: async () => Object.fromEntries((await fetchAllergyFlags(ids.slice(0, MAX_FLAGS))).items.map(i => [i.patientId, i])),
  });
  // Unsigned notes stay here, from any day, until they are signed (the list is the server's; signing elsewhere drops them on the next read).
  // Not on window focus: every read audits each note; returning from a consultation remounts Today and reads it again.
  const pending = useQuery({ queryKey: ['today-pending-notes', facilityId], enabled: online, refetchOnWindowFocus: false, queryFn: fetchPendingNotes });
  // MDV-P1-090: ended visits whose prescription waits to be authorised, beside the notes to sign; read and audited like them. A doctor the server
  // does not let prescribe (403) has none, so the section is simply absent.
  const rx = useQuery({ queryKey: ['today-pending-rx', facilityId], enabled: online, refetchOnWindowFocus: false, retry: false, queryFn: fetchPendingPrescriptions });
  // Under the board's key, so the hub push the nurse's escalation sends refetches it with the board (and the board's 30 s fallback when not live).
  const escalations = useQuery({
    queryKey: ['front-desk', facilityId, 'escalations'], enabled: online, retry: false, refetchInterval: board.live === 'live' ? false : 30_000,
    queryFn: async () => Object.fromEntries((await fetchEscalations(facilityId)).map(e => [e.appointmentId, e.reason])),
  });
  // #1365 F-061: a doctor who also works at the organisation's other locations is told when patients wait for them there. Asked only with
  // another location to ask about, each minute (no live push crosses locations). A failed read shows no line: it is a hint, the board there is the record.
  const elsewhere = useQuery({
    queryKey: ['today-elsewhere', facilityId], enabled: online && otherLocations.length > 0, retry: false, refetchInterval: 60_000,
    queryFn: () => fetchElsewhere(facilityId),
  });
  const elsewhereNow = useMemo(() => elsewhereLines(elsewhere.data ?? [], otherLocations), [elsewhere.data, otherLocations]);
  const reasons = useMemo(() => escalations.data ?? {}, [escalations.data]);
  const escalated = useMemo(() => escalatedVisits(visits, reasons), [visits, reasons]);
  return {
    // A failed read is said out loud: "no escalation" must never be a load error in disguise.
    escalations: reasons, escalationsFailed: escalations.isError, escalated, elsewhere: elsewhereNow,
    pendingRx: rx.data?.items ?? [], pendingRxMore: rx.data?.more ?? false,
    pendingRxFailed: rx.isError && !(rx.error instanceof ApiError && rx.error.status === 403),
    pendingNotes: pending.data?.items ?? [], pendingMore: pending.data?.more ?? false, pendingFailed: pending.isError,
    allergyFlags: flags.data ?? {},
    day: board.board?.date ?? null, timeZone: board.board?.timeZone ?? timeZone,
    // An escalated patient is pinned above the hero (MDV-P1-103), so the hero offers the next one who is not.
    visits, next: nextPatient(visits.filter(v => !escalated.includes(v))), scope, onScope: setScope,
    waitMinutes: (since: string) => minutesSince(since, board.now),
    loading: board.loading, offline: board.offline, online, live: board.live, forbidden: board.forbidden, failed: board.failed, onRetry: board.onRetry,
  };
}

export type TodayViewModel = ReturnType<typeof useTodayController>;
