import { addDays, type Appointment, type AppointmentState, type ScheduleColumn, type ScheduleDay, type Slot, type Unavailable } from './schedule';

// MDV-P1-023: day, week and list views of the same data. Pure, so the consistency rules are tested without a browser.
export type ScheduleView = 'day' | 'week' | 'list';
export const scheduleViews: readonly ScheduleView[] = ['day', 'week', 'list'];

/** GET /v1/schedule/week: Monday to Sunday of one calendar; each day holds only that calendar's column. */
export type ScheduleWeek = { facilityId: string; timeZone: string; calendarId: string; days: ScheduleDay[] };

/** The look of a state in @medvya/ui scheduleLooks; each look pairs an icon and words, and dashes or hatching mark the inactive ones. */
export type StateLook = 'booked' | 'checkedIn' | 'inRoom' | 'completed' | 'noShow' | 'cancelled';
const looks: Record<AppointmentState, StateLook> = {
  Booked: 'booked', CheckedIn: 'checkedIn', WithNurse: 'checkedIn', ReadyForDoctor: 'checkedIn', InConsult: 'inRoom',
  Billing: 'completed', Done: 'completed', NoShow: 'noShow', Cancelled: 'cancelled',
};
export const lookOf = (state: AppointmentState): StateLook => looks[state];

/** States that occupy time; a cancelled or no-show visit does not count as booked. */
export const occupies = (state: AppointmentState) => state !== 'Cancelled' && state !== 'NoShow';

/** The columns a filter shows: every doctor and resource, or one calendar. */
export const filterColumns = (day: ScheduleDay, calendarId: string | null): ScheduleDay =>
  calendarId ? { ...day, columns: day.columns.filter(c => c.calendarId === calendarId) } : day;

export type AgendaEntry =
  | ({ kind: 'appointment' } & Appointment)
  | ({ kind: 'slot' } & Slot)
  | ({ kind: 'unavailable' } & Unavailable);

/** List view: one section per calendar, every entry in time order (the reading order on a phone and on paper). */
export function agenda(day: ScheduleDay): { column: ScheduleColumn; entries: AgendaEntry[] }[] {
  return day.columns.map(column => ({
    column,
    entries: [
      ...column.appointments.map(a => ({ kind: 'appointment' as const, ...a })),
      ...column.slots.map(s => ({ kind: 'slot' as const, ...s })),
      ...column.unavailable.map(u => ({ kind: 'unavailable' as const, ...u })),
    ].sort((a, b) => Date.parse(a.start) - Date.parse(b.start)),
  }));
}

export type WeekDay = { date: string; working: boolean; booked: number; capacity: number; appointments: Appointment[] };

/** Week view: per day the booked count against capacity and that day's appointments, from the same columns the day view shows. */
export const weekDays = (week: ScheduleWeek): WeekDay[] =>
  week.days.map(d => {
    const c = d.columns[0];
    const appointments = c?.appointments ?? [];
    return { date: d.date, working: (c?.hours.length ?? 0) > 0, booked: appointments.filter(a => occupies(a.state)).length, capacity: c?.capacity ?? 0, appointments };
  });

/** The first free slot not being booked elsewhere and not yet started: where the pinned "+ Book" opens booking. */
export function firstFreeSlot(day: ScheduleDay, now: Date): { column: ScheduleColumn; slot: Slot } | null {
  const found = day.columns
    .flatMap(column => column.slots.filter(s => !s.held && Date.parse(s.start) >= now.getTime()).map(slot => ({ column, slot })))
    .sort((a, b) => Date.parse(a.slot.start) - Date.parse(b.slot.start));
  return found[0] ?? null;
}

/** Monday to Sunday around a YYYY-MM-DD date, as the server's week endpoint answers it; the heading shows it before the data arrives. */
export function weekSpan(date: string): { from: string; to: string } {
  const weekday = new Date(`${date}T12:00:00Z`).getUTCDay(); // 0 is Sunday
  const from = addDays(date, -((weekday + 6) % 7));
  return { from, to: addDays(from, 6) };
}
