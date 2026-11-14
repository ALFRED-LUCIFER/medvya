import type { ReasonCategory } from '../../schedule/domain/booking';
import type { AppointmentState, ScheduleColumn, VisitType } from '../../schedule/domain/schedule';
import type { FollowUpDue } from './followUpDue';

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
  /** A consultation is open for the visit (MDV-P1-100): the doctor's End moves it, the desk has no action. */
  withDoctor?: boolean;
  /** On a Done card: the doctor's follow-up still to book, date and doctor only (MDV-P1-102). */
  followUp?: FollowUpDue | null;
  /** 1, 2, … in the clinic's queue order while the patient waits (MDV-P1-103): escalations first, then the clinic's rule; null otherwise. */
  queuePlace?: number | null;
};
/** leftWithoutBeingSeen: today's patients who left before being seen; they are off the board and never in Done. */
export type Board = { facilityId: string; timeZone: string; date: string; now: string; cards: BoardCard[]; leftWithoutBeingSeen?: number };

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

/** The server's queue order (MDV-P1-103), shared by the doctor's day, the board and the queue display; a card without a place goes after. */
export const byQueuePlace = (a: BoardCard, b: BoardCard) =>
  (a.queuePlace ?? Number.MAX_SAFE_INTEGER) - (b.queuePlace ?? Number.MAX_SAFE_INTEGER) || Date.parse(a.since) - Date.parse(b.since);
const waitingColumns: readonly BoardColumn[] = ['CheckedIn', 'WithNurse', 'ReadyForDoctor'];

/**
 * Cards per column: Booked by appointment time; the waiting columns in the clinic's queue order (MDV-P1-103), so they agree with the
 * doctor's day and the queue display; the later columns in the order patients entered them.
 */
export function groupByColumn(cards: BoardCard[]): Record<BoardColumn, BoardCard[]> {
  const groups = Object.fromEntries(boardColumns.map(c => [c, [] as BoardCard[]])) as Record<BoardColumn, BoardCard[]>;
  for (const card of cards) if (isBoardColumn(card.state)) groups[card.state].push(card);
  for (const c of boardColumns)
    groups[c].sort((a, b) => c === 'Booked' ? Date.parse(a.start) - Date.parse(b.start) : waitingColumns.includes(c) ? byQueuePlace(a, b) : Date.parse(a.since) - Date.parse(b.since));
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
export const moveErrors = ['invalid_transition', 'not_today', 'intake_required', 'changed', 'undo_expired', 'with_doctor'] as const;
export type MoveError = (typeof moveErrors)[number] | 'failed';
export const moveError = (code: string | undefined): MoveError =>
  (moveErrors as readonly string[]).includes(code ?? '') ? (code as MoveError) : 'failed';

// MDV-P1-100: undo the last move, and end a waiting visit from the card. Mirrors VisitFlow (CanUndo, UndoWindow, CanLeave, CanCancel);
// the server is the authority and refuses anything these rules would hide.
/** How long the status line offers Undo; the server refuses later. */
export const undoWindowMs = 30_000;
/** A move the server would undo: backwards to Checked in, Ready for doctor, In consult or Billing (not check-in, not the nurse's handoff). */
export const canUndo = (from: BoardColumn, to: BoardColumn) =>
  (from === 'CheckedIn' || from === 'ReadyForDoctor' || from === 'InConsult' || from === 'Billing') && boardColumns.indexOf(to) > boardColumns.indexOf(from);

/** How a waiting visit ends without a consultation: the patient left before being seen, or the visit is cancelled. */
export type EndKind = 'left' | 'cancel';
/** What the card's menu offers: left for anyone waiting; cancel needs booking access and is not offered once the nurse has the patient. */
export const endKinds = (state: AppointmentState, canCancel: boolean): EndKind[] =>
  state === 'CheckedIn' || state === 'ReadyForDoctor' ? (canCancel ? ['left', 'cancel'] : ['left']) : state === 'WithNurse' ? ['left'] : [];

// MDV-P2-046: GET /v1/appointments/{id}/token-slip (IFrontDeskService.TokenSlipAsync). Nothing about the patient, by design.
export type TokenSlip = { clinicName: string; token: string; doctorName: string; issuedAt: string; timeZone: string };
/** Reprint is offered on a waiting card with a token; the server refuses a void one (cancelled, left) whatever the client shows. */
export const canReprint = (card: Pick<BoardCard, 'state' | 'token'>) => card.token !== null && waitingColumns.includes(card.state as BoardColumn);
/** The slip's print view, opened from Walk-in's result and from a waiting card. Only the opaque visit id is in the address. */
export const tokenSlipPath = (appointmentId: string) => `/front-desk/token/${appointmentId}`;

/** What the status line says after a change on this desk; `undo` is offered for 30 seconds or until the next change. */
export type Notice = { kind: 'moved' | 'undone' | EndKind; id: string; name: string; state: BoardColumn; undo: boolean };

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
