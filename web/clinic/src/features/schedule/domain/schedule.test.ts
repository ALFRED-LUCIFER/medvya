import { describe, expect, it } from 'vitest';
import { addDays, layoutDay, minSlotPx, type ScheduleDay } from './schedule';

// 09:00–13:00 and 17:00–20:00 IST with 20-minute slots (UTC+05:30), as the API sends it.
const day: ScheduleDay = {
  facilityId: 'f-1', timeZone: 'Asia/Kolkata', date: '2026-10-19', start: '2026-10-19T03:30:00Z', end: '2026-10-19T14:30:00Z',
  columns: [
    {
      calendarId: 'd-1', calendarKind: 'Doctor', name: 'Dr. Arjun Rao', slotMinutes: 20,
      hours: [{ start: '2026-10-19T03:30:00Z', end: '2026-10-19T07:30:00Z', room: 'Room 3' }, { start: '2026-10-19T11:30:00Z', end: '2026-10-19T14:30:00Z', room: 'Room 3' }],
      slots: [{ start: '2026-10-19T03:50:00Z', end: '2026-10-19T04:10:00Z' }, { start: '2026-10-19T03:30:00Z', end: '2026-10-19T03:50:00Z' }],
      unavailable: [{ start: '2026-10-19T07:30:00Z', end: '2026-10-19T11:30:00Z', reason: 'NotWorking', label: null }],
    },
    {
      calendarId: 'r-1', calendarKind: 'Resource', name: 'Procedure room', slotMinutes: null, hours: [], slots: [],
      unavailable: [{ start: '2026-10-19T03:30:00Z', end: '2026-10-19T14:30:00Z', reason: 'NoSchedule', label: null }],
    },
  ],
};

describe('day layout', () => {
  it('places blocks on one scale where the shortest slot is a full touch target', () => {
    const layout = layoutDay(day);
    const [doctor, room] = layout.columns;
    expect(layout.height).toBe(11 * 60 * (minSlotPx / 20));
    expect(doctor!.items.map(i => [i.kind, i.top, i.height])).toEqual([['slot', 0, 48], ['slot', 48, 48], ['unavailable', 240 * 2.4, 240 * 2.4]]);
    expect(doctor!.rooms).toEqual(['Room 3']);
    expect([doctor!.working, room!.working]).toEqual([true, false]);
    expect(room!.items).toHaveLength(1);
  });

  it('marks every half hour of the frame', () => {
    const marks = layoutDay(day).marks;
    expect(marks).toHaveLength(22);
    expect(marks[0]).toEqual({ at: '2026-10-19T03:30:00.000Z', top: 0 });
    expect(marks[1]!.top).toBe(72);
  });

  it('steps calendar dates without time-zone drift', () => {
    expect(addDays('2026-10-19', 1)).toBe('2026-10-20');
    expect(addDays('2026-03-01', -1)).toBe('2026-02-28');
    expect(addDays('2026-12-31', 1)).toBe('2027-01-01');
  });
});
