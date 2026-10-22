import { post, send } from '../../schedule/services/scheduleApi';
import type { Board, BoardCard, BoardColumn } from '../domain/board';

/** Today's board of the facility, in the facility's time zone (the server decides "today"). Ids only in the URL. */
export const fetchBoard = (facilityId: string) => send<Board>(`/v1/front-desk/board?${new URLSearchParams({ facilityId })}`);

/** Check-in; `elapsedMs` is how long the desk took from tapping "Check in" to confirming (a number, never patient data). */
export const checkIn = (appointmentId: string, elapsedMs: number) =>
  post<BoardCard>(`/v1/appointments/${appointmentId}/check-in`, { elapsedMs: Math.round(elapsedMs) });

export const moveVisit = (appointmentId: string, state: BoardColumn) => post<BoardCard>(`/v1/appointments/${appointmentId}/status`, { state });

/** Milliseconds on a monotonic clock, for timing a check-in. */
export const monotonicNow = () => performance.now();
