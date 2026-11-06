import { describe, expect, it } from 'vitest';
import { addDays, hasStarted, layoutDay, minSlotPx, stepDate, type ScheduleDay } from './schedule';

// 09:00–13:00 and 17:00–20:00 IST with 20-minute slots (UTC+05:30), as the API sends it.
const day: ScheduleDay = {
  facilityId: 'f-1', timeZone: 'Asia/Kolkata', date: '2026-10-19', start: '2026-10-19T03:30:00Z', end: '2026-10-19T14:30:00Z',
  columns: [
    {
      calendarId: 'd-1', calendarKind: 'Doctor', name: 'Dr. Arjun Rao', slotMinutes: 20,
      hours: [{ start: '2026-10-19T03:30:00Z', end: '2026-10-19T07:30:00Z', room: 'Room 3' }, { start: '2026-10-19T11:30:00Z', end: '2026-10-19T14:30:00Z', room: 'Room 3' }],
      slots: [{ start: '2026-10-19T03:50:00Z', end: '2026-10-19T04:10:00Z', held: true }, { start: '2026-10-19T03:30:00Z', end: '2026-10-19T03:50:00Z', held: false }],
      appointments: [{ id: 'a-1', reference: 'APT-1', patientId: 'p-1', patientName: 'Meera Iyer', visitType: 'NewConsultation', state: 'Booked', start: '2026-10-19T04:10:00Z', end: '2026-10-19T04:50:00Z' }],
      visitLengths: [{ visitType: 'NewConsultation', minutes: 40 }], capacity: 21,
      unavailable: [{ start: '2026-10-19T07:30:00Z', end: '2026-10-19T11:30:00Z', reason: 'NotWorking', label: null }],
    },
    {
      calendarId: 'r-1', calendarKind: 'Resource', name: 'Procedure room', slotMinutes: null, hours: [], slots: [], appointments: [], visitLengths: [], capacity: 0,
      unavailable: [{ start: '2026-10-19T03:30:00Z', end: '2026-10-19T14:30:00Z', reason: 'NoSchedule', label: null }],
    },
  ],
};

describe('day layout', () => {
  it('places blocks on one scale where the shortest slot is a full touch target', () => {
    const layout = layoutDay(day);
    const [doctor, room] = layout.columns;
    expect(layout.height).toBe(11 * 60 * (minSlotPx / 20));
    expect(doctor!.items.map(i => [i.kind, i.top, i.height])).toEqual([['slot', 0, 48], ['slot', 48, 48], ['appointment', 96, 96], ['unavailable', 240 * 2.4, 240 * 2.4]]); // a 40-min visit spans two slots
    expect(doctor!.rooms).toEqual(['Room 3']);
    expect([doctor!.working, room!.working]).toEqual([true, false]);
    expect(room!.items).toHaveLength(1);
  });

  it('moves a cancelled visit beside the slot it freed and beside the visit rebooked into it, never over them (MDV-P1-052)', () => {
    const at = (h: string) => `2026-10-19T${h}:00Z`;
    const visit = (id: string, state: 'Booked' | 'Cancelled', start: string, end: string) =>
      ({ id, reference: id, patientId: 'p-1', patientName: 'Synthetic', visitType: 'FollowUp' as const, state, start: at(start), end: at(end) });
    const col = day.columns[0]!;
    const items = layoutDay({ ...day, columns: [{ ...col, unavailable: [],
      slots: [{ start: at('03:30'), end: at('03:50'), held: false }, { start: at('05:30'), end: at('05:50'), held: false }],
      appointments: [visit('c-1', 'Cancelled', '03:30', '03:50'), visit('c-2', 'Cancelled', '04:10', '04:30'), visit('b-1', 'Booked', '04:10', '04:30'), visit('c-3', 'Cancelled', '06:30', '06:50')] }] })
      .columns[0]!.items.map(i => [i.kind === 'appointment' ? i.id : i.kind, i.lane]);
    expect(items).toEqual([
      ['slot', 'main'], ['c-1', 'side'], // the freed slot first, so it is also first in the tab order
      ['b-1', 'main'], ['c-2', 'side'], // rebooked: the new visit and the cancelled one side by side
      ['slot', 'full'], ['c-3', 'full'], // nothing to share with: full width
    ]);
  });

  it('marks every half hour of the frame', () => {
    const marks = layoutDay(day).marks;
    expect(marks).toHaveLength(22);
    expect(marks[0]).toEqual({ at: '2026-10-19T03:30:00.000Z', top: 0 });
    expect(marks[1]!.top).toBe(72);
  });

  it('steps from the date asked for, so a fast double click moves two days', () => {
    // The 19th is still on screen (placeholder) after Next asked for the 20th: the second Next must land on the 21st.
    expect(stepDate('2026-10-20', null, 1)).toBe('2026-10-21');
    expect(stepDate('2026-10-20', '2026-10-19', 1)).toBe('2026-10-21'); // the date asked for wins over any earlier answer
    expect(stepDate(null, '2026-10-19', -1)).toBe('2026-10-18'); // from today's answer
    expect(stepDate(null, null, 1)).toBeNull(); // today not known yet: no step
  });

  it('steps calendar dates without time-zone drift', () => {
    expect(addDays('2026-10-19', 1)).toBe('2026-10-20');
    expect(addDays('2026-03-01', -1)).toBe('2026-02-28');
    expect(addDays('2026-12-31', 1)).toBe('2027-01-01');
  });
});

describe('a slot that has started', () => {
  // 14:00 in Kolkata (UTC+05:30) is 08:30Z; 14:00 in Dubai (UTC+04) is 10:00Z. Instants, so each facility's own 14:00 passes at its own moment.
  const kolkata = { start: '2026-10-19T08:30:00Z', end: '2026-10-19T08:50:00Z' };
  const dubai = { start: '2026-10-19T10:00:00Z', end: '2026-10-19T10:20:00Z' };
  const at = (iso: string) => Date.parse(iso);

  it('is bookable until its start instant, then not', () => {
    expect(hasStarted(kolkata, at('2026-10-19T08:29:59Z'))).toBe(false);
    expect(hasStarted(kolkata, at('2026-10-19T08:30:01Z'))).toBe(true);
  });

  it('follows the facility zone, whatever zone the device is in', () => {
    const now = at('2026-10-19T09:00:00Z'); // 14:30 in Kolkata, 13:00 in Dubai
    expect(hasStarted(kolkata, now)).toBe(true);
    expect(hasStarted(dubai, now)).toBe(false);
  });
});
