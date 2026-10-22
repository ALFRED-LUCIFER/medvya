// GET /v1/schedule (src/Modules/Scheduling/Medvya.Scheduling.Application/IScheduleService.cs). Instants are UTC ISO strings;
// they are shown in the facility's IANA zone (`timeZone`), never the device's.
export type CalendarKind = 'Doctor' | 'Resource';
export type UnavailableReason = 'NotWorking' | 'Break' | 'NoSchedule';

export type Slot = { start: string; end: string };
export type WorkingWindow = Slot & { room: string | null };
export type Unavailable = Slot & { reason: UnavailableReason; label: string | null };

export type ScheduleColumn = {
  calendarId: string; calendarKind: CalendarKind; name: string; slotMinutes: number | null;
  hours: WorkingWindow[]; slots: Slot[]; unavailable: Unavailable[];
};

/** One facility day. `date` is the facility-local date (YYYY-MM-DD); start and end frame the day for every column. */
export type ScheduleDay = { facilityId: string; timeZone: string; date: string; start: string; end: string; columns: ScheduleColumn[] };

/** Where a block sits in its column: offset and height in px from the top of the day frame. */
export type Placement = { top: number; height: number };

export type ColumnItem =
  | ({ kind: 'slot' } & Slot & Placement)
  | ({ kind: 'unavailable' } & Unavailable & Placement);

export type ColumnLayout = {
  calendarId: string; calendarKind: CalendarKind; name: string; slotMinutes: number | null;
  rooms: string[]; working: boolean;
  /** In time order: the reading order for screen readers matches the visual order. */
  items: ColumnItem[];
};

export type DayLayout = { date: string; timeZone: string; height: number; marks: { at: string; top: number }[]; columns: ColumnLayout[] };

const minute = 60_000;
/** Every slot is at least 48 px tall, so each bookable slot can be a full-size touch target (44 px plus spacing). */
export const minSlotPx = 48;

const minutes = (from: string, to: string) => (Date.parse(to) - Date.parse(from)) / minute;

/** Positions every block of the day on one vertical scale, set by the shortest slot length shown. */
export function layoutDay(day: ScheduleDay): DayLayout {
  const shortest = Math.min(30, ...day.columns.flatMap(c => (c.slotMinutes ? [c.slotMinutes] : [])));
  const ppm = minSlotPx / shortest;
  const place = (s: Slot): Placement => ({ top: minutes(day.start, s.start) * ppm, height: minutes(s.start, s.end) * ppm });

  // Time marks every 30 minutes; IST (+05:30) and GST (+04:00) keep UTC half hours on local half hours.
  const marks: DayLayout['marks'] = [];
  for (let t = Math.ceil(Date.parse(day.start) / (30 * minute)) * 30 * minute; t < Date.parse(day.end); t += 30 * minute) {
    const at = new Date(t).toISOString();
    marks.push({ at, top: minutes(day.start, at) * ppm });
  }

  return {
    date: day.date, timeZone: day.timeZone, height: minutes(day.start, day.end) * ppm, marks,
    columns: day.columns.map(c => ({
      calendarId: c.calendarId, calendarKind: c.calendarKind, name: c.name, slotMinutes: c.slotMinutes,
      rooms: [...new Set(c.hours.flatMap(h => (h.room ? [h.room] : [])))],
      working: c.hours.length > 0,
      items: [
        ...c.slots.map(s => ({ kind: 'slot' as const, ...s, ...place(s) })),
        ...c.unavailable.map(u => ({ kind: 'unavailable' as const, ...u, ...place(u) })),
      ].sort((a, b) => Date.parse(a.start) - Date.parse(b.start)),
    })),
  };
}

/**
 * The date a Previous/Next step lands on. It steps from the date last asked for, never from the day still on screen while the
 * next one loads, so a fast double click moves two days. Before any date was asked for ("today"), it steps from today's answer,
 * which the caller passes as null while that answer is still a placeholder.
 */
export function stepDate(requested: string | null, today: string | null, days: number): string | null {
  const from = requested ?? today;
  return from ? addDays(from, days) : null;
}

/** The calendar date n days after a YYYY-MM-DD date. */
export function addDays(date: string, n: number): string {
  const d = new Date(`${date}T12:00:00Z`);
  d.setUTCDate(d.getUTCDate() + n);
  return d.toISOString().slice(0, 10);
}
