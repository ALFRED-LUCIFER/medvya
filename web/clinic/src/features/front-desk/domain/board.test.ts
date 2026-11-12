import { describe, expect, it } from 'vitest';
import { groupByColumn, needsRebooking, minutesSince, moveError, nextState, serverNow, visibleColumns, walkInError, type BoardCard } from './board';

describe('walk-in (MDV-P1-025)', () => {
  it('tells "no free slot" (choose a doctor to queue with) and "busy" from other failures', () => {
    expect([walkInError('no_free_slot'), walkInError('busy'), walkInError('terms_not_accepted'), walkInError('slot_unavailable'), walkInError(undefined)]).toEqual(['no_free_slot', 'busy', 'terms_not_accepted', 'failed', 'failed']);
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

  it('skips the steps the practice has off, as the server does (MDV audit UX-05)', () => {
    const off = { nurse: false, billing: false };
    expect(nextState('CheckedIn', off)).toBe('ReadyForDoctor');
    expect(nextState('InConsult', off)).toBe('Done');
    expect(nextState('WithNurse', off)).toBe('ReadyForDoctor'); // a visit left there when the step was turned off
    expect(nextState('CheckedIn', { nurse: true, billing: false })).toBe('WithNurse');
  });

  it('hides a step that is off unless a visit is still in it', () => {
    const off = { nurse: false, billing: false };
    expect(visibleColumns(off, groupByColumn([]))).toEqual(['Booked', 'CheckedIn', 'ReadyForDoctor', 'InConsult', 'Done']);
    expect(visibleColumns(off, groupByColumn([card('n', 'WithNurse', '2026-10-19T04:00:00Z')]))).toContain('WithNurse');
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

  it('orders arrived columns by arrival, so a slotless walk-in does not jump ahead of earlier check-ins (MDV-P1-052)', () => {
    const early = { ...card('early', 'CheckedIn', '2026-10-19T11:20:00Z'), since: '2026-10-19T10:50:00Z' }; // later slot, arrived first
    const queued = { ...card('queued', 'CheckedIn', '2026-10-19T11:13:00Z'), since: '2026-10-19T11:13:00Z', unscheduled: true };
    expect(groupByColumn([queued, early]).CheckedIn.map(c => c.id)).toEqual(['early', 'queued']);
  });

  it('lists visits moved off a doctor on leave in time order, outside the columns (MDV-P1-012)', () => {
    const cards = [card('b', 'NeedsRebooking', '2026-10-19T05:00:00Z'), card('a', 'NeedsRebooking', '2026-10-19T04:00:00Z'), card('c', 'Booked', '2026-10-19T03:00:00Z')];
    expect(needsRebooking(cards).map(c => c.id)).toEqual(['a', 'b']);
    expect(Object.values(groupByColumn(cards)).flat().map(c => c.id)).toEqual(['c']);
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
