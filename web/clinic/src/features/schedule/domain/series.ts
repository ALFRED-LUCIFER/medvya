import { localDate } from './changes';

// MDV-P1-026: a recurring series. Mirrors SeriesPattern / SeriesOccurrence / BookSeries in IBookingService.cs; the server is the authority.
export const maxRepeat = 52;

/** The server's preview of one occurrence: `conflict` is the 409 code that would refuse it, null when it can be booked. */
export type Occurrence = { index: number; start: string; conflict: string | null };
/** One row the desk reviews: included unless skipped; `moved` once its time was changed (not re-checked until booking). */
export type PlanRow = { start: string; conflict: string | null; include: boolean; moved: boolean };
export type SeriesResult = { seriesId: string; occurrences: { start: string; appointment: { id: string } | null; conflict: string | null }[] };
/** What the desk is told after booking: how many were booked and which starts the server refused. */
export type SeriesDone = { name: string; booked: number; refused: { start: string; conflict: string | null }[] };

/** Conflicting occurrences start skipped; the others start included. */
export const planOf = (occurrences: Occurrence[]): PlanRow[] =>
  occurrences.map(o => ({ start: o.start, conflict: o.conflict, include: o.conflict === null, moved: false }));

export const toggleRow = (plan: PlanRow[], index: number): PlanRow[] => plan.map((r, i) => (i === index ? { ...r, include: !r.include } : r));

/** Moves one occurrence to another day and/or time of day (facility-local; what is not given stays), and includes it. */
export const moveRow = (plan: PlanRow[], index: number, to: { date?: string; time?: string }, timeZone: string): PlanRow[] =>
  plan.map((r, i) => (i === index ? { start: zonedInstant(to.date ?? localDate(r.start, timeZone), to.time ?? localTime(r.start, timeZone), timeZone), conflict: null, include: true, moved: true } : r));

/** Weekdays as the server numbers them (0 = Sunday), shown Monday first. */
export const weekdayOrder = [1, 2, 3, 4, 5, 6, 0] as const;

/** A refused booking (nothing was saved): back to the review with each conflict shown; the free ones stay included. */
export const replanOf = (result: SeriesResult): PlanRow[] =>
  result.occurrences.map(o => {
    const conflict = o.conflict === 'series_not_booked' ? null : o.conflict;
    return { start: o.start, conflict, include: conflict === null, moved: false };
  });

export const includedStarts = (plan: PlanRow[]) => plan.filter(r => r.include).map(r => r.start);

export const summarise = (name: string, result: SeriesResult): SeriesDone => ({
  name,
  booked: result.occurrences.filter(o => o.appointment).length,
  refused: result.occurrences.filter(o => !o.appointment).map(o => ({ start: o.start, conflict: o.conflict })),
});

/** HH:mm on the facility clock. */
export const localTime = (iso: string, timeZone: string) =>
  new Intl.DateTimeFormat('en-GB', { timeZone, hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).format(new Date(iso));

/** The UTC instant (ISO) of a facility-local date (YYYY-MM-DD) and time (HH:mm), daylight saving included. */
export function zonedInstant(date: string, time: string, timeZone: string): string {
  const [y, m, d] = date.split('-').map(Number);
  const [hh, mm] = time.split(':').map(Number);
  const wall = Date.UTC(y, m - 1, d, hh, mm);
  const offsetAt = (ms: number) => {
    const p = Object.fromEntries(
      new Intl.DateTimeFormat('en-US', { timeZone, hourCycle: 'h23', year: 'numeric', month: 'numeric', day: 'numeric', hour: 'numeric', minute: 'numeric', second: 'numeric' })
        .formatToParts(ms).map(x => [x.type, Number(x.value)]),
    );
    return Date.UTC(p.year, p.month - 1, p.day, p.hour, p.minute, p.second) - ms;
  };
  const first = wall - offsetAt(wall);
  return new Date(wall - offsetAt(first)).toISOString(); // the second pass settles the offset on a daylight-saving day
}
