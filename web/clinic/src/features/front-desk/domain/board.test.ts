import { describe, expect, it } from 'vitest';
import { loadCatalog, makeI18n } from '@medvya/i18n';
import { aheadWords, canReprint, canUndo, endKinds, inToday, groupByColumn, isFrontDeskView, listGroups, needsRebooking, minutesSince, ageYears, moveError, nextConsultId, nextState, serverNow, visibleColumns, walkInError, type BoardCard } from './board';

describe('walk-in (MDV-P1-025)', () => {
  it('tells "no free slot" (choose a doctor to queue with) and "busy" from other failures', () => {
    expect([walkInError('no_free_slot'), walkInError('busy'), walkInError('terms_not_accepted'), walkInError('slot_unavailable'), walkInError(undefined)]).toEqual(['no_free_slot', 'busy', 'terms_not_accepted', 'failed', 'failed']);
  });
});

const card = (id: string, state: BoardCard['state'], start: string): BoardCard => ({
  id, reference: `APT-${id}`, patientId: `p-${id}`, patientName: `Patient ${id} (synthetic)`, dateOfBirth: '1972-03-12', registrationIncomplete: false,
  calendarId: 'd-1', calendarName: 'Dr. Arjun Rao (synthetic)', visitType: 'FollowUp', reasonCategory: 'General', start, end: start, state, since: start, room: null, token: null,
});

describe('undo and the card menu (MDV-P1-100)', () => {
  it('offers undo only for a move the server undoes: backwards to Checked in, Ready, In consult or Billing', () => {
    expect([canUndo('ReadyForDoctor', 'InConsult'), canUndo('InConsult', 'Done'), canUndo('Billing', 'Done'), canUndo('CheckedIn', 'WithNurse')]).toEqual([true, true, true, true]);
    expect([canUndo('Booked', 'CheckedIn'), canUndo('WithNurse', 'ReadyForDoctor'), canUndo('InConsult', 'ReadyForDoctor')]).toEqual([false, false, false]);
  });
  it('lets a waiting visit end as left; cancel needs booking access and is not offered once the nurse has the patient', () => {
    expect([endKinds('CheckedIn', true), endKinds('ReadyForDoctor', false), endKinds('WithNurse', true)]).toEqual([['left', 'cancel'], ['left'], ['left']]);
    expect([endKinds('Booked', true), endKinds('InConsult', true), endKinds('Done', true), endKinds('LeftWithoutBeingSeen', true)]).toEqual([[], [], [], []]);
  });
  it('knows the new refusals by their codes', () => {
    expect([moveError('changed'), moveError('undo_expired'), moveError('with_doctor')]).toEqual(['changed', 'undo_expired', 'with_doctor']);
  });
});

describe('token slip (MDV-P2-046)', () => {
  it('offers Reprint on a waiting card with a token only', () => {
    expect((['CheckedIn', 'WithNurse', 'ReadyForDoctor'] as const).map(s => canReprint({ state: s, token: 'A-07' }))).toEqual([true, true, true]);
    expect([canReprint({ state: 'CheckedIn', token: null }), canReprint({ state: 'Booked', token: null }), canReprint({ state: 'LeftWithoutBeingSeen', token: 'A-07' }),
      canReprint({ state: 'Cancelled', token: 'A-07' }), canReprint({ state: 'Done', token: 'A-07' })]).toEqual([false, false, false, false, false]);
  });
});

describe('one filled button for a doctor (MDV-P1-095)', () => {
  it('is the next patient: ready for the doctor first, then checked in, then with the nurse; none when nobody waits', () => {
    const at = '2026-10-19T04:00:00Z';
    const groups = (...cards: BoardCard[]) => groupByColumn(cards);
    expect(nextConsultId(groups(card('n', 'WithNurse', at), card('c', 'CheckedIn', at), card('r', 'ReadyForDoctor', at)))).toBe('r');
    expect(nextConsultId(groups(card('n', 'WithNurse', at), card('c', 'CheckedIn', at)))).toBe('c');
    expect(nextConsultId(groups(card('n', 'WithNurse', at)))).toBe('n');
    expect(nextConsultId(groups(card('i', 'InConsult', at), card('b', 'Booked', at)))).toBeNull();
  });
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

  it('orders the waiting columns by the server\'s queue place, the order the doctor\'s day and the display share (MDV-P1-103)', () => {
    const escalated = { ...card('escalated', 'WithNurse', '2026-10-19T11:20:00Z'), queuePlace: 1 }; // came last, escalated by the nurse
    const first = { ...card('first', 'WithNurse', '2026-10-19T09:00:00Z'), queuePlace: 2 };
    expect(groupByColumn([first, escalated]).WithNurse.map(c => c.id)).toEqual(['escalated', 'first']);
  });

  it('lists visits moved off a doctor on leave in time order, outside the columns (MDV-P1-012)', () => {
    const cards = [card('b', 'NeedsRebooking', '2026-10-19T05:00:00Z'), card('a', 'NeedsRebooking', '2026-10-19T04:00:00Z'), card('c', 'Booked', '2026-10-19T03:00:00Z')];
    expect(needsRebooking(cards).map(c => c.id)).toEqual(['a', 'b']);
    expect(Object.values(groupByColumn(cards)).flat().map(c => c.id)).toEqual(['c']);
  });

  it('ages in whole years on the server clock and is null without a date of birth', () => {
    const now = Date.parse('2026-10-19T04:00:00Z');
    expect(ageYears('1972-10-19', now)).toBe(54);
    expect(ageYears('1972-10-20', now)).toBe(53);
    expect(ageYears('2026-12-01', now)).toBe(0);
    expect(ageYears(null, now)).toBeNull();
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

describe('walk-in doctors (UX-10)', () => {
  const h = (start: string, end: string) => ({ start: `2026-10-19T${start}:00+05:30`, end: `2026-10-19T${end}:00+05:30` });
  it('offers a doctor with hours today that leave does not cover, and not one without hours or on leave all day', () => {
    expect(inToday({ hours: [{ ...h('09:00', '13:00'), room: null }], unavailable: [] })).toBe(true);
    expect(inToday({ hours: [], unavailable: [] })).toBe(false);
    expect(inToday({ hours: [{ ...h('09:00', '13:00'), room: null }], unavailable: [{ ...h('00:00', '23:59'), reason: 'Leave', label: null }] })).toBe(false);
    // Away for the morning session only: still in for the afternoon.
    expect(inToday({ hours: [{ ...h('09:00', '13:00'), room: null }, { ...h('16:00', '19:00'), room: null }], unavailable: [{ ...h('09:00', '13:00'), reason: 'Leave', label: null }] })).toBe(true);
  });
});

describe('how many are ahead (MDV-P1-103, decision 10)', () => {
  it('says "Next in line" for the first in the doctor\'s queue and counts the others in words, Arabic dual included', async () => {
    await loadCatalog('ar');
    expect([0, 1, 2].map(n => aheadWords(makeI18n('en', 'IN'), n))).toEqual(['Next in line', '1 ahead', '2 ahead']);
    expect([0, 1, 2, 3].map(n => aheadWords(makeI18n('ar', 'AE'), n))).toEqual(['التالي في الدور', 'قبله مريض واحد', 'قبله مريضان', 'قبله 3 مرضى']);
  });
});

describe('the list view (MDV-P2-052)', () => {
  const at = (m: number) => new Date(Date.parse('2026-10-19T03:00:00Z') + m * 60_000).toISOString();
  const off = { nurse: false, billing: false };
  // 20 booked and 6 waiting, as the ticket's example; one done.
  const cards = [
    ...Array.from({ length: 20 }, (_, i) => card(`b${i}`, 'Booked', at(60 + i * 10))),
    ...Array.from({ length: 6 }, (_, i) => ({ ...card(`w${i}`, 'ReadyForDoctor', at(i)), queuePlace: 6 - i })), // the queue rule reverses arrival
    card('d', 'Done', at(0)),
  ];

  it('puts the waiting group first, booked after the visits in progress and done last; empty groups are left out', () => {
    const { statuses, groups } = listGroups(off, groupByColumn(cards), null);
    expect(statuses).toEqual(['CheckedIn', 'ReadyForDoctor', 'InConsult', 'Booked', 'Done']); // steps that are off have no filter
    expect(groups.map(g => [g.state, g.rows.length])).toEqual([['ReadyForDoctor', 6], ['Booked', 20], ['Done', 1]]);
    expect(listGroups({ nurse: true, billing: true }, groupByColumn([]), null).statuses).toEqual(['CheckedIn', 'ReadyForDoctor', 'WithNurse', 'InConsult', 'Billing', 'Booked', 'Done']);
  });

  it('keeps the board\'s order within a group, the clinic\'s queue rule the display follows, and names the doctor', () => {
    const columns = groupByColumn(cards);
    const [waiting] = listGroups(off, columns, null).groups;
    expect(waiting!.rows.map(r => r.id)).toEqual(columns.ReadyForDoctor.map(c => c.id));
    expect(waiting!.rows.map(r => r.id)).toEqual(['w5', 'w4', 'w3', 'w2', 'w1', 'w0']);
    expect(waiting!.rows[0]!.doctor).toBe('Dr. Arjun Rao (synthetic)');
  });

  it('shows only the pressed status, even when it has no visits', () => {
    expect(listGroups(off, groupByColumn(cards), 'Booked').groups.map(g => g.state)).toEqual(['Booked']);
    expect(listGroups(off, groupByColumn(cards), 'InConsult').groups).toEqual([{ state: 'InConsult', rows: [] }]);
  });

  it('knows its three views and nothing else', () => {
    expect(['board', 'doctors', 'list'].map(isFrontDeskView)).toEqual([true, true, true]);
    expect([null, '', 'Board', 'grid'].map(isFrontDeskView)).toEqual([false, false, false, false]);
  });
});
