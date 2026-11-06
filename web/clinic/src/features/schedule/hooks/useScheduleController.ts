import { useEffect, useMemo, useRef, useState } from 'react';
import { useSearchParams } from 'react-router';
import { keepPreviousData, useMutation, useQuery } from '@tanstack/react-query';
import type { DaySheet } from '../domain/daySheet';
import { layoutDay, stepDate } from '../domain/schedule';
import { agenda, filterColumns, firstFreeSlot, readAddress, weekDays, weekSpan, type ScheduleAddress, type ScheduleView } from '../domain/views';
import { ApiError, fetchScheduleDay, fetchScheduleWeek, printDaySheet } from '../services/scheduleApi';
import { isPhoneWidth, printPage } from '../services/viewport';

/** The current instant, refreshed each minute so slots that start while the screen is open stop being offered without a reload. */
const useNow = () => {
  const [now, setNow] = useState(Date.now);
  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), 60_000);
    return () => clearInterval(id);
  }, []);
  return now;
};


/**
 * P-04 schedule of the active facility (MDV-P1-011, MDV-P1-023): day grid, week of one calendar and a list (agenda), all from
 * the same API data. `date` null means "today in the facility", which the server decides. While the next date loads, the
 * previous one stays on screen marked as updating. A doctor starts on their own column; phones start on the list.
 * Offline is shown by the shell's banner, not here. Date and view live only in the address (?date=…&view=…), so a reload or a
 * shared link opens the same day and view.
 */
export function useScheduleController(facilityId: string, userId: string) {
  const now = useNow();
  const [defaultView] = useState<ScheduleView>(() => (isPhoneWidth() ? 'list' : 'day'));
  const [params, setParams] = useSearchParams();
  const { date, view: asked } = readAddress(params), view = asked ?? defaultView;
  // The address as last written. React Router hands a functional update the params of the last render, so two clicks before
  // the next render would both step from the same date; each change builds on this instead (MDV-P1-052).
  const written = useRef(params);
  useEffect(() => { written.current = params; }, [params]); // back/forward or a link: the address wins
  const change = (next: (current: ScheduleAddress) => Partial<ScheduleAddress>) => {
    const current = readAddress(written.current), merged = { ...current, ...next(current) }, out = new URLSearchParams(written.current);
    for (const key of ['date', 'view'] as const) { const value = merged[key]; if (value) out.set(key, value); else out.delete(key); }
    written.current = out;
    setParams(out, { replace: true }); // replace: stepping through days does not fill the back button
  };
  const setDate = (next: (requested: string | null) => string | null) => change(a => ({ date: next(a.date) }));
  const setView = (next: ScheduleView) => change(() => ({ view: next }));
  const [chosen, setChosen] = useState<string | null | undefined>(undefined); // undefined: nothing chosen yet

  // The day answers the calendar list, the zone and "today" in every view; the week view reads its week beside it.
  const query = useQuery({
    queryKey: ['schedule', facilityId, date],
    queryFn: () => fetchScheduleDay(facilityId, date),
    placeholderData: keepPreviousData,
  });
  const columns = query.data?.columns ?? [];
  const filter = chosen !== undefined ? chosen : columns.some(c => c.calendarId === userId) ? userId : null;
  const weekCalendar = filter ?? columns[0]?.calendarId ?? null;

  const week = useQuery({
    queryKey: ['schedule', facilityId, 'week', weekCalendar, date],
    queryFn: () => fetchScheduleWeek(facilityId, weekCalendar!, date),
    enabled: view === 'week' && weekCalendar !== null,
    placeholderData: keepPreviousData,
  });

  const filtered = useMemo(() => (query.data ? filterColumns(query.data, filter) : null), [query.data, filter]);
  const isWeek = view === 'week';
  const current = isWeek ? week : query;
  const today = query.isPlaceholderData ? null : (query.data?.date ?? null);
  const step = (days: number) => setDate(requested => stepDate(requested, today, days) ?? requested);
  const error = query.error ?? (isWeek ? week.error : null);
  const forbidden = error instanceof ApiError && error.status === 403;

  // MDV-P1-050: the server audits the print and returns the paper fields; the sheet is shown for printing only, then dropped.
  const [sheet, setSheet] = useState<DaySheet | null>(null);
  const printing = useMutation({ mutationFn: () => printDaySheet(facilityId, date), onSuccess: setSheet });
  useEffect(() => { if (sheet) printPage(() => setSheet(null)); }, [sheet]);

  const shown = date ?? today;
  return {
    sheet,
    printing: printing.isPending,
    printFailed: printing.isError,
    onPrintSheet: () => printing.mutate(),
    view,
    // The date asked for, else today's answer: the heading never shows the stale date as if it were the new one.
    date: shown,
    weekSpan: shown ? weekSpan(shown) : null,
    timeZone: query.data?.timeZone ?? null,
    calendars: columns.map(c => ({ id: c.calendarId, name: c.name })),
    filter: isWeek ? weekCalendar : filter,
    day: useMemo(() => (filtered ? layoutDay(filtered) : null), [filtered]),
    agenda: useMemo(() => (filtered ? agenda(filtered) : []), [filtered]),
    week: useMemo(() => (week.data ? weekDays(week.data) : null), [week.data]),
    weekName: columns.find(c => c.calendarId === weekCalendar)?.name ?? '',
    weekCalendar,
    now,
    nextFree: filtered ? firstFreeSlot(filtered, new Date(now)) : null,
    updating: current.isPlaceholderData,
    loading: query.isPending,
    forbidden,
    failed: !!error && !forbidden && !current.data,
    onView: setView,
    onFilter: (calendarId: string | null) => setChosen(calendarId),
    onOpenDay: (d: string) => change(() => ({ date: d, view: 'day' })),
    onPrevious: () => step(isWeek ? -7 : -1),
    onNext: () => step(isWeek ? 7 : 1),
    onToday: () => setDate(() => null),
    onRetry: () => void (isWeek && week.error ? week.refetch() : query.refetch()),
  };
}

export type ScheduleViewModel = ReturnType<typeof useScheduleController>;
