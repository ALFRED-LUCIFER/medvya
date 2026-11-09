import { useState } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { refusalOf } from '../../clinical-access/domain/clinicalAccess';
import { fetchEncounter } from '../../encounters/services/encountersApi';
import { ApiError } from '../../schedule/services/scheduleApi';
import { draftProblems, draftSchema, emptyDraft, needsDetail, problemOf, type DecisionKind, type Draft, type Problem, type Reconciliation } from '../domain/reconciliation';
import { confirmReconciliation, decideMedicine, fetchReconciliation } from '../services/reconciliationApi';

/**
 * One consultation's medication reconciliation (E-07). The server answers every call with the whole table (conflicts, allowed decisions, counts),
 * which replaces the cached one; the screen never works a conflict or a count out itself. Keep, Add and Ignore save at once; Stop and Change open
 * a small form for the reason (and the new dose) first.
 */
export function useReconciliationController(encounterId: string) {
  const queryClient = useQueryClient();
  const key = ['reconciliation', encounterId];
  const query = useQuery({ queryKey: key, queryFn: () => fetchReconciliation(encounterId), retry: false });
  const banner = useQuery({ queryKey: ['encounter', encounterId], queryFn: () => fetchEncounter(encounterId), retry: false });
  const form = useForm<Draft>({ resolver: zodResolver(draftSchema), defaultValues: emptyDraft });
  const [open, setOpen] = useState<{ key: string; kind: DecisionKind } | null>(null);
  const [problem, setProblem] = useState<Problem | null>(null);

  const take = (view: Reconciliation) => {
    setProblem(null);
    setOpen(null);
    form.reset(emptyDraft);
    queryClient.setQueryData(key, view);
  };
  const fail = (e: unknown) => setProblem(problemOf(e instanceof ApiError ? e.code : undefined));
  const decide = useMutation({ mutationFn: (d: { key: string; kind: DecisionKind; newDose: string; reason: string }) => decideMedicine(encounterId, d), onSuccess: take, onError: fail });
  const confirm = useMutation({ mutationFn: () => confirmReconciliation(encounterId), onSuccess: take, onError: fail });

  const error = query.error instanceof ApiError ? query.error : null;
  return {
    view: query.data ?? null,
    banner: banner.data ?? null,
    loading: query.isPending,
    notFound: error?.status === 404,
    refusal: error?.status === 403 ? (refusalOf(error.body) ?? { canRequest: false, requestPending: false }) : null,
    failed: query.isError && error?.status !== 404 && error?.status !== 403,
    form,
    open,
    problem,
    saving: decide.isPending,
    confirming: confirm.isPending,
    onPick: (rowKey: string, kind: DecisionKind) => {
      setProblem(null);
      form.reset(emptyDraft);
      if (needsDetail(kind)) setOpen({ key: rowKey, kind });
      else decide.mutate({ key: rowKey, kind, ...emptyDraft });
    },
    onCancel: () => setOpen(null),
    onSave: form.handleSubmit(d => {
      if (!open) return;
      const missing = draftProblems(open.kind, d);
      missing.forEach(f => form.setError(f, { type: 'required' }));
      if (missing.length === 0) decide.mutate({ key: open.key, kind: open.kind, newDose: d.newDose, reason: d.reason });
    }),
    onConfirm: () => confirm.mutate(),
  };
}
