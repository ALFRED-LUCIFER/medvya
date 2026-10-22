import { useMemo, useState } from 'react';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { addDays, layoutDay } from '../domain/schedule';
import { ApiError, fetchScheduleDay } from '../services/scheduleApi';

/** P-04 day view of the active facility. `date` null means "today in the facility", which the server decides. */
export function useScheduleController(facilityId: string, online: boolean) {
  const [date, setDate] = useState<string | null>(null);
  const query = useQuery({
    queryKey: ['schedule', facilityId, date],
    queryFn: () => fetchScheduleDay(facilityId, date),
    placeholderData: keepPreviousData,
    retry: (count, error) => !(error instanceof ApiError && error.status === 403) && count < 2,
  });
  const day = useMemo(() => (query.data ? layoutDay(query.data) : null), [query.data]);
  const shown = query.data?.date ?? date;
  const forbidden = query.error instanceof ApiError && query.error.status === 403;

  return {
    day,
    date: shown,
    loading: query.isPending && online,
    offline: !online && !query.data,
    forbidden,
    failed: query.isError && !forbidden && !query.data,
    onPrevious: () => shown && setDate(addDays(shown, -1)),
    onNext: () => shown && setDate(addDays(shown, 1)),
    onToday: () => setDate(null),
    onRetry: () => void query.refetch(),
  };
}

export type ScheduleViewModel = ReturnType<typeof useScheduleController>;
