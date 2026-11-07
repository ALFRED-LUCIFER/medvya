import { useState } from 'react';
import { useInfiniteQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { pageSize, type AuditEntry } from '../domain/audit';
import { reasonForm, type ReasonForm } from '../../team/domain/team';
import { exportAudit, fetchAudit } from '../services/auditApi';
import { ApiError } from '../../api/services/api';

const auditKey = ['platform', 'audit'] as const;

export function useAuditController(canExport: boolean) {
  const client = useQueryClient();
  const log = useInfiniteQuery({
    queryKey: auditKey,
    queryFn: ({ pageParam }) => fetchAudit(pageParam),
    initialPageParam: null as AuditEntry | null,
    getNextPageParam: last => (last.length === pageSize ? last[last.length - 1] : undefined),
  });
  const [exporting, setExporting] = useState(false);
  const form = useForm<ReasonForm>({ resolver: zodResolver(reasonForm), defaultValues: { reason: '' } });
  const run = useMutation({
    mutationFn: ({ reason }: ReasonForm) => exportAudit(reason),
    onSuccess: () => { setExporting(false); form.reset(); void client.invalidateQueries({ queryKey: auditKey }); },
  });
  const entries = log.data?.pages.flat() ?? [];
  return {
    state: log.isPending ? 'loading' as const : log.isError ? 'error' as const : entries.length === 0 ? 'empty' as const : 'ready' as const,
    forbidden: log.error instanceof ApiError && log.error.status === 403,
    entries,
    hasMore: log.hasNextPage,
    loadingMore: log.isFetchingNextPage,
    onLoadMore: () => void log.fetchNextPage(),
    canExport, exporting, form, exportBusy: run.isPending, exportFailed: run.isError,
    onStartExport: () => { run.reset(); setExporting(true); },
    onCancelExport: () => setExporting(false),
    onExport: form.handleSubmit(values => run.mutate(values)),
  };
}

