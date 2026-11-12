import { useQuery } from '@tanstack/react-query';
import { noFilters } from '../domain/timeline';
import { fetchTimeline } from '../services/timelineApi';

/** The newest timeline entries for the one-page chart (MDV-P2-033), unfiltered; the full timeline keeps its own filters. Audited, so no refetch on focus. */
export function useTimelinePreviewController(patientId: string) {
  const q = useQuery({ queryKey: ['timeline-preview', patientId], queryFn: () => fetchTimeline(patientId, noFilters, null), retry: false, refetchOnWindowFocus: false });
  return { entries: q.data?.entries ?? [], timeZone: q.data?.timeZone ?? 'UTC', loading: q.isPending, failed: q.isError };
}
