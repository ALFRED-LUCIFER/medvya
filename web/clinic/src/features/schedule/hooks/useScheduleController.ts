import { useMemo, useState } from 'react';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { layoutDay, stepDate } from '../domain/schedule';
import { ApiError, fetchScheduleDay } from '../services/scheduleApi';

/**
 * P-04 day view of the active facility. `date` null means "today in the facility", which the server decides. While the next
 * day loads, the previous one stays on screen marked as updating. Offline is shown by the shell's banner, not here.
 */
export function useScheduleController(facilityId: string) {
  const [date, setDate] = useState<string | null>(null);
  const query = useQuery({
    queryKey: ['schedule', facilityId, date],
    queryFn: () => fetchScheduleDay(facilityId, date),
    placeholderData: keepPreviousData,
    retry: (count, error) => !(error instanceof ApiError && error.status === 403) && count < 2,
  });
  const day = useMemo(() => (query.data ? layoutDay(query.data) : null), [query.data]);
  const today = query.isPlaceholderData ? null : (query.data?.date ?? null);
  const step = (days: number) => setDate(requested => stepDate(requested, today, days) ?? requested);
  const forbidden = query.error instanceof ApiError && query.error.status === 403;

  return {
    day,
    // The date asked for, else today's answer: the heading never shows the stale day as if it were the new one.
    date: date ?? today,
    updating: query.isPlaceholderData,
    loading: query.isPending,
    forbidden,
    failed: query.isError && !forbidden && !query.data,
    onPrevious: () => step(-1),
    onNext: () => step(1),
    onToday: () => setDate(null),
    onRetry: () => void query.refetch(),
  };
}

export type ScheduleViewModel = ReturnType<typeof useScheduleController>;
