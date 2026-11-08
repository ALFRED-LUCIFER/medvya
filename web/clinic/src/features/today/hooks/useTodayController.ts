import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useBoardController } from '../../front-desk/hooks/useBoardController';
import { minutesSince } from '../../front-desk/domain/board';
import { MAX_FLAGS, nextPatient, patientIdsOf, todayVisits, type Scope } from '../domain/today';
import { fetchAllergyFlags } from '../services/todayApi';

/**
 * D-01 Doctor Today (MDV-P2-002): the board's data (same audited endpoint, live updates and server clock), narrowed to the doctor's own
 * visits, or the clinic's when toggled. The server decides who may see the board; nothing here grants anything.
 */
export function useTodayController(facilityId: string, online: boolean, userId: string) {
  const board = useBoardController(facilityId, online);
  const [scope, setScope] = useState<Scope>('mine');
  const visits = useMemo(() => todayVisits(board.board?.cards ?? [], scope, userId), [board.board, scope, userId]);
  // Only for patients the clinician may treat (the server leaves the others out); a failed lookup shows no flag, never a guess.
  const ids = useMemo(() => patientIdsOf(visits), [visits]);
  const flags = useQuery({
    queryKey: ['today-allergy-flags', facilityId, ids], enabled: online && ids.length > 0, retry: false, refetchOnWindowFocus: false,
    queryFn: async () => Object.fromEntries((await fetchAllergyFlags(ids.slice(0, MAX_FLAGS))).items.map(i => [i.patientId, i.state])),
  });
  return {
    allergyFlags: flags.data ?? {},
    day: board.board?.date ?? null, timeZone: board.board?.timeZone ?? null,
    visits, next: nextPatient(visits), scope, onScope: setScope,
    waitMinutes: (since: string) => minutesSince(since, board.now),
    loading: board.loading, offline: board.offline, online, live: board.live, forbidden: board.forbidden, failed: board.failed, onRetry: board.onRetry,
  };
}

export type TodayViewModel = ReturnType<typeof useTodayController>;
