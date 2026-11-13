import { newIdempotencyKey } from '../../api/services/idempotencyKey';
import { post, send } from '../../schedule/services/scheduleApi';
import type { Board, BoardCard, BoardColumn, WalkInChoice, WalkInVisit } from '../domain/board';
import type { ClinicOverview, ReassignReason } from '../domain/overview';

/** A walk-in: the next free slot today, or queued with the chosen doctor. Ids and list values in the body, never in the URL. */
export const walkIn = (facilityId: string, patientId: string, c: WalkInChoice, allowRepeat: boolean) =>
  post<WalkInVisit>('/v1/appointments/walk-in', { facilityId, patientId, calendarId: c.calendarId || null, visitType: c.visitType, reasonCategory: c.reasonCategory, allowRepeat });

/** Today's board of the facility, in the facility's time zone (the server decides "today"). Ids only in the URL. */
export const fetchBoard = (facilityId: string) => send<Board>(`/v1/front-desk/board?${new URLSearchParams({ facilityId })}`);

/** Appointments with a failed or bounced message to the patient (MDV-P1-034): ids only. Roles that may not see messages get 403, and the board then shows no marks. */
export const fetchFailedVisits = (facilityId: string) => send<string[]>(`/v1/notifications/failed-visits?${new URLSearchParams({ facilityId })}`);

/**
 * Check-in; `elapsedMs` is how long the desk took, from opening Walk-in or tapping "Check in" to now (a number, never patient
 * data), or null when the start was not seen: the server stores that as not measured, never as zero (MDV-P1-106).
 */
export const checkIn = (appointmentId: string, elapsedMs: number | null) =>
  post<BoardCard>(`/v1/appointments/${appointmentId}/check-in`, { elapsedMs: elapsedMs === null ? null : Math.round(elapsedMs) }, newIdempotencyKey()); // idempotent by state; the key lets the client retry it

/** Clinic overview of the facility today (MDV-P1-030). Ids only in the URL. */
export const fetchOverview = (facilityId: string) => send<ClinicOverview>(`/v1/front-desk/overview?${new URLSearchParams({ facilityId })}`);

/** Moves a waiting patient to another doctor's queue; the reason is one of the listed codes. */
export const reassignVisit = (appointmentId: string, calendarId: string, reason: ReassignReason) =>
  post<BoardCard>(`/v1/appointments/${appointmentId}/reassign`, { calendarId, reason });

export const moveVisit = (appointmentId: string, state: BoardColumn) => post<BoardCard>(`/v1/appointments/${appointmentId}/status`, { state });

/** Undo this desk's last move of the visit; `state` is the state that move left it in, so the server refuses if it has moved since (MDV-P1-100). */
export const undoMove = (appointmentId: string, state: BoardColumn) => post<BoardCard>(`/v1/appointments/${appointmentId}/undo`, { state });

/** The patient left before being seen: a reason code and an optional note in the body, never in the URL. */
export const markLeft = (appointmentId: string, reason: string, note: string) => post<unknown>(`/v1/appointments/${appointmentId}/left`, { reason, note: note || null });

/** Milliseconds on a monotonic clock, for timing a check-in. */
export const monotonicNow = () => performance.now();

// When Walk-in was opened, kept in memory while the desk is away in quick registration, so a new patient's check-in is timed from
// opening Walk-in and not from coming back (MDV-P1-106). A clock reading only: no patient data, nothing in browser storage.
let walkInStart: number | null = null;
export const holdWalkInStart = (startedAt: number | null) => { walkInStart = startedAt; };
export const heldWalkInStart = () => walkInStart;
