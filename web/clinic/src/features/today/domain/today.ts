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

/** The server answers at most this many patients per flag lookup (IPatientChart.MaxFlags). */
export const MAX_FLAGS = 100;

/** The distinct patients of the visits, in the order shown: the lookup's key, so it runs again only when the people change. */
export const patientIdsOf = (visits: BoardCard[]): string[] => [...new Set(visits.map(v => v.patientId).filter(Boolean))];

/**
 * An unsigned note of the signed-in doctor's (MDV-P2-002): the consultation to finish it in. Or, once signed, a consultation still open after
 * the facility's day it started on (MDV audit UX-11): it stays here until it is ended. The server decides both flags in the facility's time zone.
 */
export type PendingNote = { encounterId: string; startedAt: string; drafted: boolean; noteSigned: boolean; openPastDay: boolean; patientName: string | null };

/** A nurse's urgent call to the treating doctor about a visit (MDV-P2-007), shown on that visit until the consultation starts. */
export type Escalation = { id: string; appointmentId: string; reason: string; escalatedAt: string };
