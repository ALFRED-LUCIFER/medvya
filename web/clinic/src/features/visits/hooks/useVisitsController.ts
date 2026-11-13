import { useQuery } from '@tanstack/react-query';
import { fetchDeskVisits, fetchVisits } from '../services/visitsApi';

/** The chart's Visits block. Each fetch is an audited read, so no refetch on focus. */
export function useVisitsController(patientId: string) {
  const q = useQuery({ queryKey: ['visits', patientId], queryFn: () => fetchVisits(patientId), retry: false, refetchOnWindowFocus: false });
  return { visits: q.data?.visits ?? [], timeZone: q.data?.timeZone ?? 'UTC', loading: q.isPending, failed: q.isError };
}

/** The desk record's visit list: date and status only; and the doctor's follow-up still to book, date and doctor only (MDV-P1-102). */
export function useDeskVisitsController(facilityId: string, patientId: string) {
  const q = useQuery({ queryKey: ['desk-visits', facilityId, patientId], queryFn: () => fetchDeskVisits(facilityId, patientId), retry: false, refetchOnWindowFocus: false });
  return { visits: q.data?.visits ?? [], followUp: q.data?.followUp ?? null, loading: q.isPending, failed: q.isError };
}
