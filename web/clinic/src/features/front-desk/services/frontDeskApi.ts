import { post, send } from '../../schedule/services/scheduleApi';
import type { Board, BoardCard, BoardColumn, WalkInChoice, WalkInVisit } from '../domain/board';
import type { ClinicOverview, ReassignReason } from '../domain/overview';

/** A walk-in: the next free slot today, or queued with the chosen doctor. Ids and list values in the body, never in the URL. */
export const walkIn = (facilityId: string, patientId: string, c: WalkInChoice) =>
  post<WalkInVisit>('/v1/appointments/walk-in', { facilityId, patientId, calendarId: c.calendarId || null, visitType: c.visitType, reasonCategory: c.reasonCategory });

/** Today's board of the facility, in the facility's time zone (the server decides "today"). Ids only in the URL. */
export const fetchBoard = (facilityId: string) => send<Board>(`/v1/front-desk/board?${new URLSearchParams({ facilityId })}`);

/** Check-in; `elapsedMs` is how long the desk took from tapping "Check in" to confirming (a number, never patient data). */
export const checkIn = (appointmentId: string, elapsedMs: number) =>
  post<BoardCard>(`/v1/appointments/${appointmentId}/check-in`, { elapsedMs: Math.round(elapsedMs) });

/** Clinic overview of the facility today (MDV-P1-030). Ids only in the URL. */
export const fetchOverview = (facilityId: string) => send<ClinicOverview>(`/v1/front-desk/overview?${new URLSearchParams({ facilityId })}`);

/** Moves a waiting patient to another doctor's queue; the reason is one of the listed codes. */
export const reassignVisit = (appointmentId: string, calendarId: string, reason: ReassignReason) =>
  post<BoardCard>(`/v1/appointments/${appointmentId}/reassign`, { calendarId, reason });

export const moveVisit = (appointmentId: string, state: BoardColumn) => post<BoardCard>(`/v1/appointments/${appointmentId}/status`, { state });

/** Milliseconds on a monotonic clock, for timing a check-in. */
export const monotonicNow = () => performance.now();
