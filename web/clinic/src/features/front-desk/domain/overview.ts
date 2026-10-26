import { z } from 'zod';
import type { AppointmentState } from '../../schedule/domain/schedule';
import { boardColumns, type BoardCard, type BoardColumn } from './board';

// GET /v1/front-desk/overview (IFrontDeskService.cs, MDV-P1-030). Staff only, nonclinical like the board.
export type DoctorLoad = {
  calendarId: string; name: string;
  counts: Record<BoardColumn, number>;
  /** Minutes running late (server estimate); 0 when on time. */
  delayMinutes: number;
  next: { id: string; start: string; state: AppointmentState; patientName: string; token: string | null }[];
};
export type WaitingVisit = { id: string; token: string | null; patientName: string; calendarId: string; state: AppointmentState; start: string; since: string };
export type ClinicOverview = { facilityId: string; timeZone: string; now: string; doctors: DoctorLoad[]; waiting: WaitingVisit[] };

/** Why a waiting patient moves to another doctor: a fixed list, no free text (ReassignReason). */
export const reassignReasons = ['DoctorRunningLate', 'DoctorUnavailable', 'PatientRequest', 'BalanceWorkload'] as const;
export type ReassignReason = (typeof reassignReasons)[number];

export const reassignSchema = z.object({ calendarId: z.string().min(1), reason: z.enum(reassignReasons) });
export type ReassignInput = z.infer<typeof reassignSchema>;

const waitingStates: readonly AppointmentState[] = ['CheckedIn', 'WithNurse', 'ReadyForDoctor'];
export const isWaiting = (state: AppointmentState) => waitingStates.includes(state);

/**
 * How late a doctor is running, as the server's ClinicDelay: the later of how late the current consultation started and how
 * long the earliest waiting patient is past their time. Whole minutes, never negative.
 */
export function delayMinutes(cards: Pick<BoardCard, 'start' | 'state' | 'since'>[], now: number) {
  const consult = Math.max(0, ...cards.filter(c => c.state === 'InConsult').map(c => Date.parse(c.since) - Date.parse(c.start)));
  const waiting = Math.max(0, ...cards.filter(c => isWaiting(c.state) && Date.parse(c.start) <= now).map(c => now - Date.parse(c.start)));
  return Math.floor(Math.max(consult, waiting) / 60_000);
}

/** The board's "Doctor workload" panel (P-01): per doctor with visits today, how many wait and how late they run. */
export function workload(cards: BoardCard[], now: number) {
  const doctors = [...new Map(cards.map(c => [c.calendarId, c.calendarName])).entries()];
  const rows = doctors.map(([id, name]) => {
    const own = cards.filter(c => c.calendarId === id);
    return { calendarId: id, name, waiting: own.filter(c => isWaiting(c.state)).length, delayMinutes: delayMinutes(own, now) };
  });
  const most = Math.max(1, ...rows.map(r => r.waiting));
  return rows.map(r => ({ ...r, share: r.waiting / most }));
}

export const countsInOrder = (counts: Record<BoardColumn, number>) => boardColumns.map(state => ({ state, count: counts[state] ?? 0 }));
