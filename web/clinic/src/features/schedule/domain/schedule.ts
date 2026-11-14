import { localDate } from './changes';

// GET /v1/schedule (src/Modules/Scheduling/Medvya.Scheduling.Application/IScheduleService.cs). Instants are UTC ISO strings;
// they are shown in the facility's IANA zone (`timeZone`), never the device's.
export type CalendarKind = 'Doctor' | 'Resource';
export type UnavailableReason = 'NotWorking' | 'Break' | 'NoSchedule' | 'Closed' | 'Leave';
export type VisitType = 'FollowUp' | 'NewConsultation' | 'Procedure' | 'Teleconsult';
export type AppointmentState = 'Booked' | 'CheckedIn' | 'WithNurse' | 'ReadyForDoctor' | 'InConsult' | 'Billing' | 'Done' | 'Cancelled' | 'NoShow' | 'NeedsRebooking' | 'LeftWithoutBeingSeen';

/** States that occupy time; a cancelled, no-show or needs-rebooking visit does not count as booked. */
export const occupies = (state: AppointmentState) => state !== 'Cancelled' && state !== 'NoShow' && state !== 'NeedsRebooking' && state !== 'LeftWithoutBeingSeen';

export type Span = { start: string; end: string };
/** A slot that has started (compared as instants, so the facility zone cannot change the answer); the server refuses it, so the screens do not offer it. */
export const hasStarted = (slot: Span, now: number) => Date.parse(slot.start) < now;
/** A free slot; `held` means someone else is booking it right now. */
export type Slot = Span & { held: boolean };
export type WorkingWindow = Span & { room: string | null };
export type Unavailable = Span & { reason: UnavailableReason; label: string | null };
/** Never carries the free-text reason: reception sees name, type, time and state only. */
export type Appointment = Span & { id: string; reference: string; patientId: string; patientName: string; visitType: VisitType; state: AppointmentState; seriesId?: string | null };
export type VisitLength = { visitType: VisitType; minutes: number };

export type ScheduleColumn = {
  calendarId: string; calendarKind: CalendarKind; name: string; slotMinutes: number | null;
  hours: WorkingWindow[]; slots: Slot[]; unavailable: Unavailable[]; appointments: Appointment[]; visitLengths: VisitLength[];
  /** Template slots that day, booked or free. */
  capacity: number;
};

/** One facility day. `date` is the facility-local date (YYYY-MM-DD); start and end frame the day for every column. */
export type ScheduleDay = { facilityId: string; timeZone: string; date: string; start: string; end: string; columns: ScheduleColumn[] };

/**
 * Where a block sits in its column: offset and height in px from the top of the day frame. `lane` splits the width when a visit
 * that no longer occupies time (cancelled, no-show) shares its time with a slot or another visit: it moves to the `side` lane and
 * the other block keeps the `main` lane, so a freed slot is never under a card (MDV-P1-052).
 */
export type Placement = { top: number; height: number; lane: 'full' | 'main' | 'side' };

export type ColumnItem =
  | ({ kind: 'slot' } & Slot & Placement)
  | ({ kind: 'appointment' } & Appointment & Placement)
  | ({ kind: 'unavailable' } & Unavailable & Placement);

export type ColumnLayout = {
  calendarId: string; calendarKind: CalendarKind; name: string; slotMinutes: number | null;
  rooms: string[]; working: boolean; visitLengths: VisitLength[];
  /** In time order: the reading order for screen readers matches the visual order. */
  items: ColumnItem[];
};

export type DayLayout = { date: string; timeZone: string; height: number; marks: { at: string; top: number }[]; columns: ColumnLayout[] };

const minute = 60_000;
/** Every slot is at least 48 px tall, so each bookable slot can be a full-size touch target (44 px plus spacing). */
export const minSlotPx = 48;

const minutes = (from: string, to: string) => (Date.parse(to) - Date.parse(from)) / minute;

/**
 * The grid track of each column (MDV audit UX-05): a calendar not working that day and with no visits shrinks to a narrow
 * labelled strip, so the doctors who are in get the width.
 */
export const columnTracks = (columns: ColumnLayout[]) =>
  columns.map(c => (c.working || c.items.some(i => i.kind === 'appointment') ? 'minmax(168px, 1fr)' : '112px')).join(' ');

/** Positions every block of the day on one vertical scale, set by the shortest slot length shown. */
export function layoutDay(day: ScheduleDay): DayLayout {
  const shortest = Math.min(30, ...day.columns.flatMap(c => (c.slotMinutes ? [c.slotMinutes] : [])));
  const ppm = minSlotPx / shortest;
  const place = (s: Span) => ({ top: minutes(day.start, s.start) * ppm, height: minutes(s.start, s.end) * ppm });

  // A time mark per slot of the shortest length, on that column's slot starts, so ruler labels sit on slot rows (MDV-P1-052).
  const step = shortest * minute, from = Date.parse(day.start);
  const firstSlot = day.columns.find(c => c.slotMinutes === shortest && c.hours.length > 0)?.hours[0]?.start;
  const anchor = firstSlot ? Date.parse(firstSlot) : from;
  const marks: DayLayout['marks'] = [];
  for (let t = anchor - Math.floor((anchor - from) / step) * step; t < Date.parse(day.end); t += step) {
    const at = new Date(t).toISOString();
    marks.push({ at, top: minutes(day.start, at) * ppm });
  }

  return {
    date: day.date, timeZone: day.timeZone, height: minutes(day.start, day.end) * ppm, marks,
    columns: day.columns.map(c => ({
      calendarId: c.calendarId, calendarKind: c.calendarKind, name: c.name, slotMinutes: c.slotMinutes, visitLengths: c.visitLengths,
      rooms: [...new Set(c.hours.flatMap(h => (h.room ? [h.room] : [])))],
      working: c.hours.length > 0,
      items: lanes([
        ...c.slots.map(s => ({ kind: 'slot' as const, ...s, ...place(s) })),
        ...c.appointments.map(a => ({ kind: 'appointment' as const, ...a, ...place(a) })),
        ...c.unavailable.map(u => ({ kind: 'unavailable' as const, ...u, ...place(u) })),
      ]),
    })),
  };
}

/**
 * Today's day view opens at now (MDV-P1-097): the rows before the current slot row fold away, every block moving up by the cut, and
 * `earlier` counts the visits folded with them. Null when there is nothing to fold: another day, or before the first row. Another day
 * already opens at its first working hour, since the server starts the frame there.
 */
export function foldPast(day: DayLayout, now: number): { day: DayLayout; earlier: number } | null {
  const mark = day.marks.findLast(m => Date.parse(m.at) <= now);
  if (!mark || mark.top <= 0 || localDate(new Date(now).toISOString(), day.timeZone) !== day.date) return null;
  const at = Date.parse(mark.at), cut = mark.top;
  // Compared as instants, not pixels, so rounding never leaves a sliver of a folded block.
  const folded = (i: ColumnItem) => Date.parse(i.end) <= at;
  return {
    earlier: day.columns.reduce((n, c) => n + c.items.filter(i => i.kind === 'appointment' && folded(i)).length, 0),
    day: {
      ...day, height: day.height - cut,
      marks: day.marks.filter(m => m.top >= cut).map(m => ({ ...m, top: m.top - cut })),
      // A block running across the cut (a visit in progress, a break) stays, cut at the top.
      columns: day.columns.map(c => ({ ...c, items: c.items.filter(i => !folded(i)).map(i => ({ ...i, top: Math.max(0, i.top - cut), height: Math.min(i.height, i.top + i.height - cut) })) })),
    },
  };
}

type Unlaned = (({ kind: 'slot' } & Slot) | ({ kind: 'appointment' } & Appointment) | ({ kind: 'unavailable' } & Unavailable)) & { top: number; height: number };
const overlaps = (a: Span, b: Span) => Date.parse(a.start) < Date.parse(b.end) && Date.parse(b.start) < Date.parse(a.end);
const aside = (i: Unlaned) => i.kind === 'appointment' && !occupies(i.state);

/** Time order, with the live block first on a tie, so the free slot comes before the cancelled visit in the reading order too. */
function lanes(items: Unlaned[]): ColumnItem[] {
  return items
    .map(i => ({ ...i, lane: aside(i)
      ? (items.some(o => !aside(o) && overlaps(o, i)) ? 'side' : 'full')
      : (items.some(o => aside(o) && overlaps(o, i)) ? 'main' : 'full') }) as ColumnItem)
    .sort((a, b) => Date.parse(a.start) - Date.parse(b.start) || Number(aside(a)) - Number(aside(b)));
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
