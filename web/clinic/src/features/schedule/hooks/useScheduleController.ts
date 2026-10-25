import { useMemo, useState } from 'react';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { layoutDay, stepDate } from '../domain/schedule';
import { agenda, filterColumns, firstFreeSlot, weekDays, weekSpan, type ScheduleView } from '../domain/views';
import { ApiError, fetchScheduleDay, fetchScheduleWeek } from '../services/scheduleApi';
import { isPhoneWidth } from '../services/viewport';

const noRetryOn403 = (count: number, error: unknown) => !(error instanceof ApiError && error.status === 403) && count < 2;

/**
 * P-04 schedule of the active facility (MDV-P1-011, MDV-P1-023): day grid, week of one calendar and a list (agenda), all from
 * the same API data. `date` null means "today in the facility", which the server decides. While the next date loads, the
 * previous one stays on screen marked as updating. A doctor starts on their own column; phones start on the list.
 * Offline is shown by the shell's banner, not here.
 */
export function useScheduleController(facilityId: string, userId: string) {
  const [date, setDate] = useState<string | null>(null);
  const [view, setView] = useState<ScheduleView>(() => (isPhoneWidth() ? 'list' : 'day'));
  const [chosen, setChosen] = useState<string | null | undefined>(undefined); // undefined: nothing chosen yet

  // The day answers the calendar list, the zone and "today" in every view; the week view reads its week beside it.
  const query = useQuery({
    queryKey: ['schedule', facilityId, date],
    queryFn: () => fetchScheduleDay(facilityId, date),
    placeholderData: keepPreviousData,
    retry: noRetryOn403,
  });
  const columns = query.data?.columns ?? [];
  const filter = chosen !== undefined ? chosen : columns.some(c => c.calendarId === userId) ? userId : null;
  const weekCalendar = filter ?? columns[0]?.calendarId ?? null;

  const week = useQuery({
    queryKey: ['schedule', facilityId, 'week', weekCalendar, date],
    queryFn: () => fetchScheduleWeek(facilityId, weekCalendar!, date),
    enabled: view === 'week' && weekCalendar !== null,
    placeholderData: keepPreviousData,
    retry: noRetryOn403,
  });

  const filtered = useMemo(() => (query.data ? filterColumns(query.data, filter) : null), [query.data, filter]);
  const isWeek = view === 'week';
  const current = isWeek ? week : query;
  const today = query.isPlaceholderData ? null : (query.data?.date ?? null);
  const step = (days: number) => setDate(requested => stepDate(requested, today, days) ?? requested);
  const error = query.error ?? (isWeek ? week.error : null);
  const forbidden = error instanceof ApiError && error.status === 403;

  const shown = date ?? today;
  return {
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
    // "Now" is when this data arrived: pure during render, and a slot that started since is refused by the server anyway.
    nextFree: filtered ? firstFreeSlot(filtered, new Date(query.dataUpdatedAt)) : null,
    updating: current.isPlaceholderData,
    loading: query.isPending,
    forbidden,
    failed: !!error && !forbidden && !current.data,
    onView: setView,
    onFilter: (calendarId: string | null) => setChosen(calendarId),
    onOpenDay: (d: string) => { setDate(d); setView('day'); },
    onPrevious: () => step(isWeek ? -7 : -1),
    onNext: () => step(isWeek ? 7 : 1),
    onToday: () => setDate(null),
    onRetry: () => void (isWeek && week.error ? week.refetch() : query.refetch()),
  };
}

export type ScheduleViewModel = ReturnType<typeof useScheduleController>;
