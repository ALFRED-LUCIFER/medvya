import type { ReasonCategory } from '../../schedule/domain/booking';
import type { AppointmentState, VisitType } from '../../schedule/domain/schedule';

// GET /v1/front-desk/board (src/Modules/Scheduling/Medvya.Scheduling.Application/IFrontDeskService.cs). Nonclinical only:
// the reason is a category, never the patient's words. Instants are UTC; they are shown in the facility's zone.
export type BoardCard = {
  id: string; reference: string; patientId: string; patientName: string;
  /** YYYY-MM-DD, to confirm identity at check-in; null when the patient record was not found. */
  dateOfBirth: string | null;
  registrationIncomplete: boolean;
  calendarId: string; calendarName: string; visitType: VisitType; reasonCategory: ReasonCategory;
  start: string; end: string; state: AppointmentState;
  /** When the visit entered its current state. */
  since: string;
  /** Where the patient was called in to (set at call-in); null before. */
  room: string | null;
  /** Arrived without an appointment (MDV-P1-025); unscheduled = queued with no free slot (holds no slot). */
  walkIn?: boolean;
  unscheduled?: boolean;
};
export type Board = { facilityId: string; timeZone: string; date: string; now: string; cards: BoardCard[] };

/** Real-time connection to the board hub (MDV-P1-029): live, or paused (offline, dropped, refused) while the board refreshes on a timer. */
export type LiveStatus = 'connecting' | 'live' | 'paused';

/** Wait before reconnect attempt `attempt` (0, 1, …): 0.5 s doubling to at most 30 s, forever, since a desk board stays open all day. */
export const retryDelay = (attempt: number) => Math.min(30_000, 500 * 2 ** attempt);

/** The board's columns in visit order (docs/03-architecture/state-machines.md). */
export const boardColumns = ['Booked', 'CheckedIn', 'WithNurse', 'ReadyForDoctor', 'InConsult', 'Billing', 'Done'] as const satisfies readonly AppointmentState[];
export type BoardColumn = (typeof boardColumns)[number];

/** The next column, as the server's VisitFlow; null at Done. */
export const nextState = (state: BoardColumn): BoardColumn | null => boardColumns[boardColumns.indexOf(state) + 1] ?? null;

export const isBoardColumn = (state: AppointmentState): state is BoardColumn => (boardColumns as readonly string[]).includes(state);

/** Cards per column, each column in time order. */
export function groupByColumn(cards: BoardCard[]): Record<BoardColumn, BoardCard[]> {
  const groups = Object.fromEntries(boardColumns.map(c => [c, [] as BoardCard[]])) as Record<BoardColumn, BoardCard[]>;
  for (const card of cards) if (isBoardColumn(card.state)) groups[card.state].push(card);
  for (const c of boardColumns) groups[c].sort((a, b) => Date.parse(a.start) - Date.parse(b.start));
  return groups;
}

/** Whole minutes since `since`, never negative (a device clock behind the server's). */
export const minutesSince = (since: string, now: number) => Math.max(0, Math.floor((now - Date.parse(since)) / 60_000));

/**
 * The server's clock now, from the board's `now` and how long ago the board was fetched on this device, so wait timers do not
 * depend on the device's clock being right.
 */
export const serverNow = (board: Pick<Board, 'now'>, fetchedAt: number, deviceNow: number) => Date.parse(board.now) + Math.max(0, deviceNow - fetchedAt);

/** 409 codes from the API (VisitTransitionException); anything else is a generic failure. */
export const moveErrors = ['invalid_transition', 'not_today'] as const;
export type MoveError = (typeof moveErrors)[number] | 'failed';
export const moveError = (code: string | undefined): MoveError =>
  (moveErrors as readonly string[]).includes(code ?? '') ? (code as MoveError) : 'failed';

// MDV-P1-025 walk-in: POST /v1/appointments/walk-in (IBookingService.WalkInAsync), then the board's check-in.
/** calendarId "" = the next available doctor. */
export type WalkInChoice = { calendarId: string; visitType: VisitType; reasonCategory: ReasonCategory };
export type WalkInVisit = { id: string; calendarId: string; start: string; end: string; walkIn: boolean; unscheduled: boolean; patientName: string };
export const emptyWalkIn: WalkInChoice = { calendarId: '', visitType: 'FollowUp', reasonCategory: 'General' };

/** 409 codes of a walk-in: nothing free today (the desk then chooses a doctor to queue with), or the calendar is busy. */
export const walkInErrors = ['no_free_slot', 'busy'] as const;
export type WalkInError = (typeof walkInErrors)[number] | 'failed';
export const walkInError = (code: string | undefined): WalkInError =>
  (walkInErrors as readonly string[]).includes(code ?? '') ? (code as WalkInError) : 'failed';
