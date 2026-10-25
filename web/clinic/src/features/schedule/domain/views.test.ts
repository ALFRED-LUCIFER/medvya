import { describe, expect, it } from 'vitest';
import type { Appointment, ScheduleColumn, ScheduleDay } from './schedule';
import { agenda, filterColumns, firstFreeSlot, lookOf, weekDays, weekSpan, type ScheduleWeek } from './views';

const appt = (id: string, start: string, state: Appointment['state'] = 'Booked'): Appointment =>
  ({ id, reference: `APT-${id}`, patientId: 'p', patientName: `Patient ${id}`, visitType: 'FollowUp', state, start, end: start });
const column = (calendarId: string, extra: Partial<ScheduleColumn> = {}): ScheduleColumn => ({
  calendarId, calendarKind: 'Doctor', name: calendarId, slotMinutes: 20, hours: [{ start: '2026-10-19T03:30:00Z', end: '2026-10-19T07:30:00Z', room: null }],
  slots: [], unavailable: [], appointments: [], visitLengths: [], capacity: 12, ...extra,
});
const day = (date: string, columns: ScheduleColumn[]): ScheduleDay => ({ facilityId: 'f', timeZone: 'Asia/Kolkata', date, start: '', end: '', columns });

describe('schedule views', () => {
  it('gives every state a look with a shape cue, never colour alone', () => {
    expect([lookOf('Booked'), lookOf('CheckedIn'), lookOf('InConsult'), lookOf('Done'), lookOf('NoShow'), lookOf('Cancelled')])
      .toEqual(['booked', 'checkedIn', 'inRoom', 'completed', 'noShow', 'cancelled']);
  });

  it('lists every appointment, free slot and unavailable block of the day in time order', () => {
    const d = day('2026-10-19', [column('d-1', {
      appointments: [appt('b', '2026-10-19T04:10:00Z')],
      slots: [{ start: '2026-10-19T03:30:00Z', end: '2026-10-19T03:50:00Z', held: false }],
      unavailable: [{ start: '2026-10-19T07:30:00Z', end: '2026-10-19T11:30:00Z', reason: 'NotWorking', label: null }],
    }), column('d-2')]);
    expect(agenda(d).map(s => s.entries.map(e => e.kind))).toEqual([['slot', 'appointment', 'unavailable'], []]);
    expect(filterColumns(d, 'd-2').columns.map(c => c.calendarId)).toEqual(['d-2']);
    expect(filterColumns(d, null).columns).toHaveLength(2);
  });

  it('shows in the week exactly the appointments of each day, counting only those that take time', () => {
    const week: ScheduleWeek = {
      facilityId: 'f', timeZone: 'Asia/Kolkata', calendarId: 'd-1',
      days: [
        day('2026-10-19', [column('d-1', { appointments: [appt('a', '2026-10-19T03:30:00Z'), appt('x', '2026-10-19T04:30:00Z', 'Cancelled')] })]),
        day('2026-10-24', [column('d-1', { hours: [], capacity: 0 })]),
      ],
    };
    const [monday, saturday] = weekDays(week);
    expect(monday).toMatchObject({ booked: 1, capacity: 12, working: true });
    expect(monday!.appointments.map(a => a.id)).toEqual(week.days[0]!.columns[0]!.appointments.map(a => a.id));
    expect(saturday).toMatchObject({ booked: 0, capacity: 0, working: false });
  });

  it('heads the week Monday to Sunday, as the server answers it', () => {
    expect(weekSpan('2026-10-21')).toEqual({ from: '2026-10-19', to: '2026-10-25' }); // Wednesday
    expect(weekSpan('2026-10-19')).toEqual({ from: '2026-10-19', to: '2026-10-25' }); // Monday
    expect(weekSpan('2026-10-25')).toEqual({ from: '2026-10-19', to: '2026-10-25' }); // Sunday ends the week
    expect(weekSpan('2026-03-01')).toEqual({ from: '2026-02-23', to: '2026-03-01' }); // across a month end
  });

  it('opens "+ Book" at the first free slot that has not started and is not being booked elsewhere', () => {
    const d = day('2026-10-19', [
      column('d-1', { slots: [{ start: '2026-10-19T03:30:00Z', end: '', held: false }, { start: '2026-10-19T05:00:00Z', end: '', held: true }] }),
      column('d-2', { slots: [{ start: '2026-10-19T04:50:00Z', end: '', held: false }] }),
    ]);
    const found = firstFreeSlot(d, new Date('2026-10-19T04:00:00Z'));
    expect([found?.column.calendarId, found?.slot.start]).toEqual(['d-2', '2026-10-19T04:50:00Z']);
    expect(firstFreeSlot(d, new Date('2026-10-19T06:00:00Z'))).toBeNull();
  });
});
