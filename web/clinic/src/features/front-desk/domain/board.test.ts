import { describe, expect, it } from 'vitest';
import { groupByColumn, minutesSince, moveError, nextState, serverNow, walkInError, type BoardCard } from './board';

describe('walk-in (MDV-P1-025)', () => {
  it('tells "no free slot" (choose a doctor to queue with) and "busy" from other failures', () => {
    expect([walkInError('no_free_slot'), walkInError('busy'), walkInError('slot_unavailable'), walkInError(undefined)]).toEqual(['no_free_slot', 'busy', 'failed', 'failed']);
  });
});

const card = (id: string, state: BoardCard['state'], start: string): BoardCard => ({
  id, reference: `APT-${id}`, patientId: `p-${id}`, patientName: `Patient ${id} (synthetic)`, dateOfBirth: '1972-03-12', registrationIncomplete: false,
  calendarId: 'd-1', calendarName: 'Dr. Arjun Rao (synthetic)', visitType: 'FollowUp', reasonCategory: 'General', start, end: start, state, since: start, room: null, token: null,
});

describe('reception board', () => {
  it('steps one column at a time and stops at Done', () => {
    expect(nextState('Booked')).toBe('CheckedIn');
    expect(nextState('InConsult')).toBe('Billing');
    expect(nextState('Billing')).toBe('Done');
    expect(nextState('Done')).toBeNull();
  });

  it('groups cards per column in time order and leaves cancelled and no-show visits off the board', () => {
    const groups = groupByColumn([
      card('b', 'Booked', '2026-10-19T04:30:00Z'), card('a', 'Booked', '2026-10-19T03:30:00Z'),
      card('c', 'CheckedIn', '2026-10-19T04:00:00Z'), card('x', 'Cancelled', '2026-10-19T03:00:00Z'), card('y', 'NoShow', '2026-10-19T03:00:00Z'),
    ]);
    expect(groups.Booked.map(c => c.id)).toEqual(['a', 'b']);
    expect(groups.CheckedIn.map(c => c.id)).toEqual(['c']);
    expect(Object.values(groups).flat()).toHaveLength(3);
    expect(groups.Done).toEqual([]);
  });

  it('counts whole waiting minutes on the server clock, never below zero', () => {
    const since = '2026-10-19T04:00:00Z';
    expect(minutesSince(since, Date.parse('2026-10-19T04:12:59Z'))).toBe(12);
    expect(minutesSince(since, Date.parse('2026-10-19T03:59:00Z'))).toBe(0);
    // The device clock is 3 hours off; only the time elapsed on the device since the fetch counts.
    const fetchedAt = Date.parse('2026-10-19T07:00:00Z');
    expect(serverNow({ now: '2026-10-19T04:05:00Z' }, fetchedAt, fetchedAt + 60_000)).toBe(Date.parse('2026-10-19T04:06:00Z'));
    expect(serverNow({ now: '2026-10-19T04:05:00Z' }, fetchedAt, fetchedAt - 5_000)).toBe(Date.parse('2026-10-19T04:05:00Z')); // a tick older than the fetch
  });

  it('maps conflict codes and treats anything unknown as a failure', () => {
    expect(moveError('invalid_transition')).toBe('invalid_transition');
    expect(moveError('not_today')).toBe('not_today');
    expect(moveError('something_new')).toBe('failed');
    expect(moveError(undefined)).toBe('failed');
  });
});
