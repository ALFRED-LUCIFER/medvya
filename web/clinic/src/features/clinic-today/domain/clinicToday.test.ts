import { describe, expect, it } from 'vitest';
import { attention, type ClinicToday } from './clinicToday';

const today: ClinicToday = {
  now: '2026-11-14T05:00:00Z', timeZone: 'Asia/Kolkata', booked: 3, toArrive: 1, arrivedWalkIn: 1, arrivedBooked: 2, waiting: 2, longestWaitMinutes: 25,
  incompleteRegistrations: 4,
  doctors: [{ id: 'me', name: 'Dr. Arjun Rao', notesToSign: 2, notesBeforeToday: 1, toAuthorise: 1 }, { id: 'other', name: 'Dr. S. Menon', notesToSign: 5, notesBeforeToday: 5, toAuthorise: 0 }],
};
const all = { myDay: true, directory: true, waitingRoom: true };

describe('attention', () => {
  it('lists the signed-in doctor\'s own backlog, incomplete registrations and the waiting room, each once', () => {
    expect(attention(today, 'me', all)).toEqual([
      { kind: 'mine', notes: 2, prescriptions: 1 }, { kind: 'incomplete', count: 4 }, { kind: 'waiting', count: 2, minutes: 25 },
    ]);
  });

  it('leaves out a line the person has no screen to act on, and nothing that is zero', () => {
    expect(attention(today, 'admin', all).map(l => l.kind)).toEqual(['incomplete', 'waiting']); // not a doctor: no own backlog
    expect(attention(today, 'me', { myDay: false, directory: false, waitingRoom: false })).toEqual([]);
    expect(attention({ ...today, incompleteRegistrations: 0, waiting: 0, longestWaitMinutes: null, doctors: [] }, 'me', all)).toEqual([]);
  });
});
