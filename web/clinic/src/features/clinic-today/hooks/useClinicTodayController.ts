import { useQuery } from '@tanstack/react-query';
import { ApiError } from '../../schedule/services/scheduleApi';
import { attention, type AttentionScreens } from '../domain/clinicToday';
import { fetchClinicToday } from '../services/clinicTodayApi';

/** The owner's Clinic today (MDV-P2-053): read only, refreshed every minute while open. The server decides who may see it. */
export function useClinicTodayController(facilityId: string, userId: string, screens: AttentionScreens) {
  const query = useQuery({ queryKey: ['clinic-today', facilityId], queryFn: () => fetchClinicToday(facilityId), refetchInterval: 60_000 });
  const today = query.data ?? null;
  const forbidden = query.error instanceof ApiError && query.error.status === 403;
  return {
    state: today ? ('ready' as const) : query.isPending ? ('loading' as const) : forbidden ? ('forbidden' as const) : ('error' as const),
    today,
    lines: today ? attention(today, userId, screens) : [],
    onRetry: () => void query.refetch(),
  };
}

export type ClinicTodayViewModel = ReturnType<typeof useClinicTodayController>;
