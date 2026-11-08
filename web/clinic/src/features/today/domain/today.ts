import { isBoardColumn, type BoardCard } from '../../front-desk/domain/board';

/** D-01 Today shows the doctor's own visits by default and the whole clinic (D-04) when toggled. */
export type Scope = 'mine' | 'clinic';

/** A doctor's calendar id is their user id (as the clinic overview's "mine" reads it). Visits the leave left without a slot are not today's. */
export const todayVisits = (cards: BoardCard[], scope: Scope, userId: string): BoardCard[] =>
  cards.filter(c => isBoardColumn(c.state) && (scope === 'clinic' || c.calendarId === userId)).sort((a, b) => Date.parse(a.start) - Date.parse(b.start));

/** Who the hero offers: the consultation in progress, then the longest-waiting arrived patient, then the earliest booked one. */
const heroOrder = ['InConsult', 'ReadyForDoctor', 'WithNurse', 'CheckedIn', 'Booked'] as const;
export function nextPatient(visits: BoardCard[]): BoardCard | null {
  for (const state of heroOrder) {
    const key = (c: BoardCard) => Date.parse(state === 'Booked' ? c.start : c.since);
    const first = visits.filter(v => v.state === state).sort((a, b) => key(a) - key(b))[0];
    if (first) return first;
  }
  return null;
}

/** Whole years on `day` (YYYY-MM-DD, the facility's today); null when the date of birth is unknown. */
export function ageOn(dateOfBirth: string | null, day: string): number | null {
  if (!dateOfBirth) return null;
  const [y, m, d] = dateOfBirth.split('-').map(Number), [ty, tm, td] = day.split('-').map(Number);
  return ty! - y! - (tm! < m! || (tm === m && td! < d!) ? 1 : 0);
}
