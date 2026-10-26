import { describe, expect, it } from 'vitest';
import type { BoardCard } from './board';
import { delayMinutes, reassignSchema, workload } from './overview';

const nine = Date.parse('2026-10-19T03:30:00Z'); // 09:00 IST
const at = (minutes: number) => new Date(nine + minutes * 60_000).toISOString();
const card = (id: string, calendarId: string, state: BoardCard['state'], start: number, since = start): BoardCard => ({
  id, reference: id, patientId: `p-${id}`, patientName: `Patient ${id} (synthetic)`, dateOfBirth: null, registrationIncomplete: false,
  calendarId, calendarName: calendarId === 'd-1' ? 'Dr. Arjun Rao (synthetic)' : 'Dr. S. Menon (synthetic)', visitType: 'FollowUp',
  reasonCategory: 'General', start: at(start), end: at(start + 20), state, since: at(since), room: null, token: null,
});

describe('clinic overview', () => {
  it('runs late by the later of a late consultation and the longest wait past its time, like the server', () => {
    expect(delayMinutes([], nine)).toBe(0);
    expect(delayMinutes([card('a', 'd-1', 'CheckedIn', 30)], nine)).toBe(0); // early arrival
    expect(delayMinutes([card('a', 'd-1', 'InConsult', 0, 15)], nine + 20 * 60_000)).toBe(15);
    expect(delayMinutes([card('a', 'd-1', 'InConsult', 0, 15), card('b', 'd-1', 'ReadyForDoctor', 20, 10)], nine + 60 * 60_000)).toBe(40);
    expect(delayMinutes([card('a', 'd-1', 'Booked', 0), card('b', 'd-1', 'Done', 0, 120)], nine + 180 * 60_000)).toBe(0);
  });

  it('shows each doctor with patients waiting and their share of the longest queue', () => {
    const rows = workload([card('a', 'd-1', 'CheckedIn', 0), card('b', 'd-1', 'WithNurse', 20), card('c', 'd-2', 'ReadyForDoctor', 40), card('d', 'd-2', 'Done', 0)], nine + 30 * 60_000);
    expect(rows).toEqual([
      { calendarId: 'd-1', name: 'Dr. Arjun Rao (synthetic)', waiting: 2, delayMinutes: 30, share: 1 },
      { calendarId: 'd-2', name: 'Dr. S. Menon (synthetic)', waiting: 1, delayMinutes: 0, share: 0.5 },
    ]);
  });

  it('a move needs a doctor and one of the listed reasons, never free text', () => {
    expect(reassignSchema.safeParse({ calendarId: 'd-2', reason: 'DoctorRunningLate' }).success).toBe(true);
    expect(reassignSchema.safeParse({ calendarId: 'd-2', reason: 'He has a fever' }).success).toBe(false);
    expect(reassignSchema.safeParse({ calendarId: '', reason: 'PatientRequest' }).success).toBe(false);
  });
});
