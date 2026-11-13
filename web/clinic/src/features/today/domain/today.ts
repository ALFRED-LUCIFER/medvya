import { byQueuePlace, isBoardColumn, type BoardCard } from '../../front-desk/domain/board';

/** D-01 Today shows the doctor's own visits by default and the whole clinic (D-04) when toggled. */
export type Scope = 'mine' | 'clinic';

// The day reads top down: who is with the doctor, the waiting patients in the clinic's queue order, the booked ones by time, then the rest.
const stage = (c: BoardCard) => (c.state === 'InConsult' ? 0 : c.queuePlace != null ? 1 : c.state === 'Booked' ? 2 : 3);

/**
 * A doctor's calendar id is their user id (as the clinic overview's "mine" reads it). Visits the leave left without a slot are not today's.
 * Waiting patients follow the server's queue place (MDV-P1-103), so the day agrees with the board and the queue display.
 */
export const todayVisits = (cards: BoardCard[], scope: Scope, userId: string): BoardCard[] =>
  cards.filter(c => isBoardColumn(c.state) && (scope === 'clinic' || c.calendarId === userId)).sort((a, b) =>
    stage(a) - stage(b) || (stage(a) === 1 ? byQueuePlace(a, b) : stage(a) === 2 ? Date.parse(a.start) - Date.parse(b.start) : Date.parse(a.since) - Date.parse(b.since)));

/** Who the hero offers: the consultation in progress, then the first ready patient in queue order, then those still with the nurse or checked in, then the earliest booked. */
const heroOrder = ['InConsult', 'ReadyForDoctor', 'WithNurse', 'CheckedIn', 'Booked'] as const;
export function nextPatient(visits: BoardCard[]): BoardCard | null {
  for (const state of heroOrder) {
    const first = visits.filter(v => v.state === state).sort((a, b) => state === 'Booked' ? Date.parse(a.start) - Date.parse(b.start) : byQueuePlace(a, b))[0];
    if (first) return first;
  }
  return null;
}

/** Visits a nurse escalated (MDV-P1-103), pinned above Next patient in queue order until the consultation starts. */
export const escalatedVisits = (visits: BoardCard[], escalations: Record<string, string>): BoardCard[] =>
  visits.filter(v => escalations[v.id] && v.state !== 'InConsult');

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
