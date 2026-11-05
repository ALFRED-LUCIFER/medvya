import { useState } from 'react';
import { useInfiniteQuery, useMutation, useQuery } from '@tanstack/react-query';
import { ApiError } from '../../schedule/services/scheduleApi';
import { usePatientSearch } from '../../patients/hooks/usePatientSearch';
import { fetchStaff } from '../../staff/services/staffApi';
import { defaultFilters, type AuditFilters } from '../domain/auditLog';
import { downloadAuditCsv, fetchAuditPage } from '../services/auditLogApi';

/** Settings → Audit log: filters (staff, patient, action, dates), the newest-first list with "load more", and the CSV export. */
export function useAuditLogController() {
  const [filters, setFilters] = useState<AuditFilters>(() => defaultFilters());
  const set = (patch: Partial<AuditFilters>) => setFilters(f => ({ ...f, ...patch }));
  const search = usePatientSearch(id => set({ patientId: id }));
  const staff = useQuery({ queryKey: ['staff', 'list'], queryFn: fetchStaff });
  const log = useInfiniteQuery({
    queryKey: ['audit', 'access-log', filters],
    queryFn: ({ pageParam }) => fetchAuditPage(filters, pageParam),
    initialPageParam: null as string | null,
    getNextPageParam: last => last.next,
    retry: (count, e) => !(e instanceof ApiError && e.status === 403) && count < 2,
    staleTime: 0,
  });
  const exporting = useMutation({ mutationFn: () => downloadAuditCsv(filters) });

  const pages = log.data?.pages ?? [];
  const rows = pages.flatMap(p => p.items);
  return {
    filters,
    people: [...new Map((staff.data ?? []).filter(s => s.userId).map(s => [s.userId!, s.name ?? s.email ?? ''])).entries()].map(([id, name]) => ({ id, name })),
    actions: pages[0]?.actions ?? [],
    rows,
    // The name arrives only with a chosen patient; the first row that has it labels the filter.
    patientName: filters.patientId ? (rows.find(r => r.patientName)?.patientName ?? null) : null,
    patientSearch: search.view,
    state: log.isPending ? ('loading' as const) : log.error instanceof ApiError && log.error.status === 403 ? ('forbidden' as const) : log.isError ? ('error' as const) : ('ready' as const),
    canLoadMore: log.hasNextPage,
    loadingMore: log.isFetchingNextPage,
    exporting: exporting.isPending,
    exportFailed: exporting.isError,
    onFilters: set,
    onClearPatient: () => { set({ patientId: null }); search.clear(); },
    onMore: () => void log.fetchNextPage(),
    onExport: () => exporting.mutate(),
  };
}
