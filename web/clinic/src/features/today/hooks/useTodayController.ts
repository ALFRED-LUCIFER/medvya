import { useMemo, useState } from 'react';
import { useBoardController } from '../../front-desk/hooks/useBoardController';
import { minutesSince } from '../../front-desk/domain/board';
import { nextPatient, todayVisits, type Scope } from '../domain/today';

/**
 * D-01 Doctor Today (MDV-P2-002): the board's data (same audited endpoint, live updates and server clock), narrowed to the doctor's own
 * visits, or the clinic's when toggled. The server decides who may see the board; nothing here grants anything.
 */
export function useTodayController(facilityId: string, online: boolean, userId: string) {
  const board = useBoardController(facilityId, online);
  const [scope, setScope] = useState<Scope>('mine');
  const visits = useMemo(() => todayVisits(board.board?.cards ?? [], scope, userId), [board.board, scope, userId]);
  return {
    day: board.board?.date ?? null, timeZone: board.board?.timeZone ?? null,
    visits, next: nextPatient(visits), scope, onScope: setScope,
    waitMinutes: (since: string) => minutesSince(since, board.now),
    loading: board.loading, offline: board.offline, online, live: board.live, forbidden: board.forbidden, failed: board.failed, onRetry: board.onRetry,
  };
}

export type TodayViewModel = ReturnType<typeof useTodayController>;
