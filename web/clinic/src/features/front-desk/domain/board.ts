import type { ReasonCategory } from '../../schedule/domain/booking';
import type { AppointmentState, ScheduleColumn, VisitType } from '../../schedule/domain/schedule';

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
  /** The waiting-room token from check-in ("A-07", MDV-P1-031); null before. What the queue TV shows instead of a name. */
  token: string | null;
  /** Arrived without an appointment (MDV-P1-025); unscheduled = queued with no free slot (holds no slot). */
  walkIn?: boolean;
  unscheduled?: boolean;
  /** Sex at birth, with the age a desk tells two patients of one name apart by (MDV-AUDIT-1105); absent when the record was not found. */
  sex?: 'Female' | 'Male' | 'Other' | null;
};
export type Board = { facilityId: string; timeZone: string; date: string; now: string; cards: BoardCard[] };

/** Real-time connection to the board hub (MDV-P1-029): live, or paused (offline, dropped, refused) while the board refreshes on a timer. */
export type LiveStatus = 'connecting' | 'live' | 'paused';

/** Wait before reconnect attempt `attempt` (0, 1, …): 0.5 s doubling to at most 30 s, forever, since a desk board stays open all day. */
export const retryDelay = (attempt: number) => Math.min(30_000, 500 * 2 ** attempt);

/** The board's columns in visit order (docs/03-architecture/state-machines.md). */
export const boardColumns = ['Booked', 'CheckedIn', 'WithNurse', 'ReadyForDoctor', 'InConsult', 'Billing', 'Done'] as const satisfies readonly AppointmentState[];
export type BoardColumn = (typeof boardColumns)[number];

/** The optional visit steps the practice has on (flags clinical.nurse-intake and billing.invoices); as the server's VisitSteps. */
export type VisitSteps = { nurse: boolean; billing: boolean };
export const allSteps: VisitSteps = { nurse: true, billing: true };
const hasStep = (state: BoardColumn, steps: VisitSteps) => (state === 'WithNurse' ? steps.nurse : state === 'Billing' ? steps.billing : true);

/** The next column, as the server's VisitFlow: a step that is off is skipped (MDV audit UX-05); null at Done. */
export const nextState = (state: BoardColumn, steps: VisitSteps = allSteps): BoardColumn | null =>
  boardColumns.slice(boardColumns.indexOf(state) + 1).find(c => hasStep(c, steps)) ?? null;

/** The columns to show: a step that is off is hidden, unless a visit is still in it (the step was turned off that day). */
export const visibleColumns = (steps: VisitSteps, columns: Record<BoardColumn, BoardCard[]>) =>
  boardColumns.filter(c => hasStep(c, steps) || columns[c].length > 0);

export const isBoardColumn = (state: AppointmentState): state is BoardColumn => (boardColumns as readonly string[]).includes(state);

/**
 * Cards per column: Booked by appointment time; every later column in the order patients entered it, so Checked in follows the
 * token order and a walk-in queued "now" with no slot does not jump ahead of patients who arrived earlier (MDV-P1-052).
 */
export function groupByColumn(cards: BoardCard[]): Record<BoardColumn, BoardCard[]> {
  const groups = Object.fromEntries(boardColumns.map(c => [c, [] as BoardCard[]])) as Record<BoardColumn, BoardCard[]>;
  for (const card of cards) if (isBoardColumn(card.state)) groups[card.state].push(card);
  for (const c of boardColumns) groups[c].sort((a, b) => c === 'Booked' ? Date.parse(a.start) - Date.parse(b.start) : Date.parse(a.since) - Date.parse(b.since));
  return groups;
}

/** Visits the doctor's leave left without a slot (MDV-P1-012): they wait for the desk to move or cancel them, and leave the columns. */
export const needsRebooking = (cards: BoardCard[]) => cards.filter(c => c.state === 'NeedsRebooking').sort((a, c) => Date.parse(a.start) - Date.parse(c.start));

/** Whole years from a YYYY-MM-DD date of birth to the server's `now` (UTC dates); null without a date of birth. */
export function ageYears(dateOfBirth: string | null, now: number): number | null {
  if (!dateOfBirth) return null;
  const [y, m, d] = dateOfBirth.split('-').map(Number) as [number, number, number];
  const today = new Date(now);
  const years = today.getUTCFullYear() - y;
  return Math.max(0, today.getUTCMonth() + 1 < m || (today.getUTCMonth() + 1 === m && today.getUTCDate() < d) ? years - 1 : years);
}

/** Whole minutes since `since`, never negative (a device clock behind the server's). */
export const minutesSince = (since: string, now: number) => Math.max(0, Math.floor((now - Date.parse(since)) / 60_000));

/**
 * The server's clock now, from the board's `now` and how long ago the board was fetched on this device, so wait timers do not
 * depend on the device's clock being right.
 */
export const serverNow = (board: Pick<Board, 'now'>, fetchedAt: number, deviceNow: number) => Date.parse(board.now) + Math.max(0, deviceNow - fetchedAt);

/** 409 codes from the API (VisitTransitionException); anything else is a generic failure. */
export const moveErrors = ['invalid_transition', 'not_today', 'intake_required'] as const;
export type MoveError = (typeof moveErrors)[number] | 'failed';
export const moveError = (code: string | undefined): MoveError =>
  (moveErrors as readonly string[]).includes(code ?? '') ? (code as MoveError) : 'failed';

// MDV-P1-025 walk-in: POST /v1/appointments/walk-in (IBookingService.WalkInAsync), then the board's check-in.
/** calendarId "" = the next available doctor. */
export type WalkInChoice = { calendarId: string; visitType: VisitType; reasonCategory: ReasonCategory };
export type WalkInVisit = { id: string; calendarId: string; start: string; end: string; walkIn: boolean; unscheduled: boolean; patientName: string };
export const emptyWalkIn: WalkInChoice = { calendarId: '', visitType: 'FollowUp', reasonCategory: 'General' };
/** Just registered at the desk: a first visit, not a follow-up. */
export const newPatientWalkIn: WalkInChoice = { ...emptyWalkIn, visitType: 'NewConsultation' };

/** A doctor the walk-in may name (UX-10): working hours today that leave and other blocks do not cover from start to end. */
export const inToday = (c: Pick<ScheduleColumn, 'hours' | 'unavailable'>) =>
  c.hours.some(h => !c.unavailable.some(u => Date.parse(u.start) <= Date.parse(h.start) && Date.parse(u.end) >= Date.parse(h.end)));

/** 409 codes of a walk-in: nothing free today (the desk then chooses a doctor to queue with), or the calendar is busy. */
// already_today: the patient has an open visit today; the desk confirms before a second one is made (MDV-P1-052).
export const walkInErrors = ['no_free_slot', 'busy', 'terms_not_accepted', 'already_today'] as const;
export type WalkInError = (typeof walkInErrors)[number] | 'failed';
export const walkInError = (code: string | undefined): WalkInError =>
  (walkInErrors as readonly string[]).includes(code ?? '') ? (code as WalkInError) : 'failed';
