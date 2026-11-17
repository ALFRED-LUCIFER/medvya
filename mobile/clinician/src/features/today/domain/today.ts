import type { Schemas } from '@medvya/api-client';
import { initialsOf } from '../../session/domain/session';

export type Sex = 'F' | 'M';
export type QueueStatus = 'withDoctor' | 'next' | 'waiting' | 'booked';
/** The server's allergy answer (POST /v1/chart/allergy-flags). "Not recorded" is never worded as "none". */
export type AllergyState = 'NotRecorded' | 'NoKnownAllergies' | 'Listed';
/** A clinical flag on the up-next card. `kind` picks the icon, so it is never colour alone. */
export type Flag = { kind: 'allergy' | 'result' | 'brief'; label: string } | { kind: 'allergies'; state: AllergyState };

/** `age` and `sex` are null when the server has no patient record for the visit: unknowns stay blank. `place` is the server's queue place. */
export type QueueEntry = { token: string; patientId: string; name: string; age: number | null; sex: Sex | null; note: string; status: QueueStatus; at: string; place?: number | null; viaApp?: boolean };
export type Today = {
  next: { patientId: string; name: string; initials: string; age: number | null; sex: Sex | null; reason: string; room: string; flags: Flag[] } | null;
  /** `toReview` is null when it is not known (front desk, or the count failed): shown blank, never as 0. */
  seen: number; waiting: number; toReview: number | null;
  queue: QueueEntry[];
};

/** The queue in the order the desk works it: with the doctor, next, waiting, then booked; within each, the server's queue place, then time. */
const rank: Record<QueueStatus, number> = { withDoctor: 0, next: 1, waiting: 2, booked: 3 };
/** Today as the screen gets it: `savedAt` is set when this is the offline last copy (ADR-011 §3), null when live. */
export type TodayCopy = { today: Today; savedAt: number | null };

export const orderQueue = (q: QueueEntry[]) =>
  [...q].sort((a, b) => rank[a.status] - rank[b.status] || (a.place ?? Infinity) - (b.place ?? Infinity) || a.at.localeCompare(b.at));

// GET /v1/front-desk/board (the web's board and Today read the same audited endpoint), typed from the generated client. Instants are UTC.
export type BoardCard = Schemas['BoardCard'];
export type Board = Schemas['FrontDeskBoard'];

const statusOf: Partial<Record<string, QueueStatus>> = { InConsult: 'withDoctor', ReadyForDoctor: 'next', WithNurse: 'waiting', CheckedIn: 'waiting', Booked: 'booked' };
const sexOf = (s: BoardCard['sex']): Sex | null => (s === 'Female' ? 'F' : s === 'Male' ? 'M' : null);

/** Whole years on `day` (YYYY-MM-DD, the facility's today); null when the date of birth is unknown. As the web's ageOn. */
export function ageOn(dateOfBirth: string | null, day: string): number | null {
  if (!dateOfBirth) return null;
  const [y, m, d] = dateOfBirth.split('-').map(Number), [ty, tm, td] = day.split('-').map(Number);
  return ty! - y! - (tm! < m! || (tm === m && td! < d!) ? 1 : 0);
}

/**
 * The board as the phone's Today: a doctor's own visits (`calendarId`, their user id, as the web's "mine"), or the whole
 * clinic's for front desk (null). Visits that left the queue (billing, done) count as seen. `reasonText` words a reason category.
 * The flags and the count to review come from other calls (services/today.ts).
 */
export function todayFromBoard(board: Board, calendarId: string | null, reasonText: (category: string) => string): Today {
  const cards = board.cards.filter(c => calendarId === null || c.calendarId === calendarId);
  const time = new Intl.DateTimeFormat('en-GB', { hour: '2-digit', minute: '2-digit', timeZone: board.timeZone });
  const visits = cards.flatMap(c => {
    const status = statusOf[c.state];
    if (!status) return [];
    const entry: QueueEntry = { token: c.token ?? '', patientId: c.patientId, name: c.patientName, age: ageOn(c.dateOfBirth, board.date), sex: sexOf(c.sex),
      note: time.format(new Date(c.start)), status, at: c.start, place: c.queuePlace ?? null };
    return [{ entry, card: c }];
  });
  const queue = orderQueue(visits.map(v => v.entry));
  const first = queue[0], card = visits.find(v => v.entry === first)?.card;
  return {
    next: first && card ? { patientId: first.patientId, name: first.name, initials: initialsOf(first.name), age: first.age, sex: first.sex,
      reason: reasonText(card.reasonCategory), room: card.room ?? '', flags: [] } : null,
    seen: cards.filter(c => c.state === 'Billing' || c.state === 'Done').length,
    waiting: queue.filter(q => q.status === 'next' || q.status === 'waiting').length,
    toReview: null,
    queue,
  };
}
