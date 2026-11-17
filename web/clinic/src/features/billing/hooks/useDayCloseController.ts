import { useState } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useI18n } from '@medvya/i18n';
import { useForm, useWatch } from 'react-hook-form';
import { ApiError } from '../../api/services/apiClient';
import { useTableController } from '../../table/hooks/useTableController';
import {
  countedMinor, dayCloseProblem, floatMinor, fromCounts, openSchema, sessionSort, toCounts, variance,
  type CashSession, type CountForm, type DayCloseProblem, type OpenForm,
} from '../domain/dayClose';
import { confirmCashSession, countCashSession, downloadShiftReport, fetchCashDay, openCashSession } from '../services/cashApi';

/**
 * Day close (P-07, P-13, MDV-P4-012): open the drawer with a float, count it at day end against the expected cash, and, when it differs, a note and a
 * second staff member's confirmation in their own session; the next drawer can open meanwhile. Closed sessions give a shift report. The server checks every rule again.
 */
export function useDayCloseController() {
  const { t } = useI18n();
  const queryClient = useQueryClient();
  const key = ['billing', 'cash-day'];
  const day = useQuery({ queryKey: key, queryFn: fetchCashDay });
  const current = day.data?.current ?? null;
  const awaiting = day.data?.awaiting ?? [];
  // shortcut: one confirm button, for the first count this user may confirm; a second waiting count shows after it. Fine while a desk rarely has two.
  const confirmable = awaiting.find(s => s.mayConfirm) ?? null;
  const denominations = day.data?.denominations ?? [];
  const [problem, setProblem] = useState<DayCloseProblem | null>(null);

  const done = () => { setProblem(null); void queryClient.invalidateQueries({ queryKey: key }); };
  const failed = (e: unknown) => {
    const p = dayCloseProblem(e instanceof ApiError ? e.code : undefined);
    setProblem(p);
    if (p === 'stale' || p === 'state' || p === 'already_open') void day.refetch();
  };
  const openForm = useForm<OpenForm>({ resolver: zodResolver(openSchema), defaultValues: { float: '' } });
  const countForm = useForm<CountForm>({ values: { counts: current ? fromCounts(current.counts) : {}, note: current?.note ?? '' } });
  const typed = useWatch({ control: countForm.control, name: 'counts' }) ?? {};
  const note = useWatch({ control: countForm.control, name: 'note' }) ?? '';
  const counted = countedMinor(denominations, typed);
  const off = current ? variance(counted, current.expectedMinor) : 0;

  const open = useMutation({ mutationFn: (f: OpenForm) => openCashSession(floatMinor(f)), onSuccess: done, onError: failed });
  const count = useMutation({
    mutationFn: (f: CountForm) => countCashSession(current!.id, { counts: toCounts(denominations, f.counts), note: f.note.trim() || null, version: current!.version }),
    onSuccess: done, onError: failed,
  });
  const confirm = useMutation({ mutationFn: (s: CashSession) => confirmCashSession(s.id, s.version), onSuccess: done, onError: failed });
  const report = useMutation({ mutationFn: downloadShiftReport, onError: failed });
  const table = useTableController(day.data?.recent ?? [], sessionSort, c => t(`billing.dayClose.col.${c}`));

  return {
    status: day.isPending ? ('loading' as const) : day.error instanceof ApiError && day.error.status === 403 ? ('forbidden' as const) : day.isError ? ('error' as const) : ('ready' as const),
    currency: day.data?.currency ?? 'INR', current, awaiting, confirmable, denominations, problem,
    openForm, opening: open.isPending, onOpen: openForm.handleSubmit(f => open.mutate(f)),
    countForm, counted, variance: off,
    // A variance needs its note before the count is sent (the server refuses it too).
    noteMissing: off !== 0 && note.trim() === '',
    counting: count.isPending, onCount: countForm.handleSubmit(f => count.mutate(f)),
    confirming: confirm.isPending, onConfirm: () => confirmable && confirm.mutate(confirmable),
    table, empty: (day.data?.recent.length ?? 0) === 0,
    reporting: report.isPending ? report.variables : null, onReport: (id: string) => report.mutate(id),
  };
}

export type DayCloseView = ReturnType<typeof useDayCloseController>;
