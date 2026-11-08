import { describe, expect, it } from 'vitest';
import type { BoardCard } from '../../front-desk/domain/board';
import { ageOn, MAX_FLAGS, nextPatient, patientIdsOf, todayVisits } from './today';

const card = (id: string, calendarId: string, state: BoardCard['state'], start: string, since = start): BoardCard => ({
  id, reference: id, patientId: `p-${id}`, patientName: id, dateOfBirth: null, registrationIncomplete: false, calendarId, calendarName: calendarId,
  visitType: 'FollowUp', reasonCategory: 'General', start, end: start, state, since, room: null, token: null,
});

describe('todayVisits', () => {
  const cards = [card('b', 'me', 'Booked', '2026-10-19T05:00:00Z'), card('a', 'me', 'Booked', '2026-10-19T04:00:00Z'), card('c', 'other', 'Booked', '2026-10-19T04:30:00Z'),
    card('r', 'me', 'NeedsRebooking', '2026-10-19T06:00:00Z')];
  it('shows only my visits by default, in time order, and the whole clinic when toggled', () => {
    expect(todayVisits(cards, 'mine', 'me').map(c => c.id)).toEqual(['a', 'b']);
    expect(todayVisits(cards, 'clinic', 'me').map(c => c.id)).toEqual(['a', 'c', 'b']);
  });
});

describe('nextPatient', () => {
  it('offers the consultation in progress first, then the longest waiting arrival, then the earliest booking', () => {
    const waiting = [card('w2', 'me', 'ReadyForDoctor', '2026-10-19T04:00:00Z', '2026-10-19T04:20:00Z'), card('w1', 'me', 'ReadyForDoctor', '2026-10-19T04:10:00Z', '2026-10-19T04:10:00Z'),
      card('b', 'me', 'Booked', '2026-10-19T03:00:00Z')];
    expect(nextPatient(waiting)?.id).toBe('w1');
    expect(nextPatient([...waiting, card('c', 'me', 'InConsult', '2026-10-19T03:30:00Z')])?.id).toBe('c');
    expect(nextPatient([waiting[2]!, card('b0', 'me', 'Booked', '2026-10-19T02:00:00Z')])?.id).toBe('b0');
    expect(nextPatient([card('d', 'me', 'Done', '2026-10-19T03:00:00Z')])).toBeNull();
  });
});

describe('ageOn', () => {
  it('counts whole years and is unknown without a date of birth', () => {
    expect(ageOn('1972-03-12', '2026-03-11')).toBe(53);
    expect(ageOn('1972-03-12', '2026-03-12')).toBe(54);
    expect(ageOn(null, '2026-03-12')).toBeNull();
  });
});

describe('patientIdsOf', () => {
  it('lists each patient once, in the order shown, so the flag lookup runs again only when the people change', () => {
    const twice = [card('a', 'me', 'Booked', '2026-10-19T04:00:00Z'), card('b', 'me', 'Booked', '2026-10-19T05:00:00Z'), { ...card('c', 'me', 'Booked', '2026-10-19T06:00:00Z'), patientId: 'p-a' }];
    expect(patientIdsOf(twice)).toEqual(['p-a', 'p-b']);
    expect(patientIdsOf([{ ...twice[0]!, patientId: '' }])).toEqual([]); // a visit without a patient has nothing to look up
  });
  it('stays inside what the server answers per lookup', () => expect(MAX_FLAGS).toBe(100));
});
