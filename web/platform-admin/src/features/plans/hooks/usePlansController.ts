import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { editFor, formFor, groupPlans, planForm, type PlanForm, type PlanVersion } from '../domain/plans';
import { fetchPlans, saveVersion } from '../services/plansApi';
import { ApiError } from '../../api/services/api';

const plansKey = ['platform', 'plans'] as const;

/** Plans (MDV-P5-042): every role reads; owner and finance edit (the server decides; canEdit only hides what would answer 403). */
export function usePlansController(canEdit: boolean) {
  const client = useQueryClient();
  const versions = useQuery({ queryKey: plansKey, queryFn: fetchPlans });
  const plans = versions.data ? groupPlans(versions.data) : [];
  const [editing, setEditing] = useState<PlanVersion | null>(null);
  const [historyCode, setHistoryCode] = useState<string | null>(null);
  const [saved, setSaved] = useState<PlanVersion | null>(null);
  const form = useForm<PlanForm>({ resolver: zodResolver(planForm), shouldFocusError: false });

  const save = useMutation({
    mutationFn: ({ base, values }: { base: PlanVersion; values: PlanForm }) => saveVersion(base.code, editFor(base, values)),
    onSuccess: version => { setSaved(version); setEditing(null); void client.invalidateQueries({ queryKey: plansKey }); },
  });
  const stale = save.error instanceof ApiError && save.error.status === 409;

  return {
    state: versions.isPending ? 'loading' as const : versions.isError ? 'error' as const : plans.length === 0 ? 'empty' as const : 'ready' as const,
    forbidden: versions.error instanceof ApiError && versions.error.status === 403,
    plans, canEdit,
    editing, form, busy: save.isPending,
    // 409: someone saved a newer version since this form opened. Reloading shows it; the form keeps what was typed.
    stale, failed: save.isError && !stale,
    onEdit: (v: PlanVersion) => { save.reset(); setSaved(null); setHistoryCode(null); form.reset(formFor(v)); setEditing(v); },
    onCancel: () => setEditing(null),
    onSave: form.handleSubmit(values => { if (editing) save.mutate({ base: editing, values }); }),
    onReload: () => { void client.invalidateQueries({ queryKey: plansKey }).then(() => {
      const latest = client.getQueryData<PlanVersion[]>(plansKey)?.find(v => v.code === editing?.code);
      if (latest) { save.reset(); setEditing(latest); }
    }); },
    saved, onDismissSaved: () => setSaved(null),
    history: plans.find(p => p.code === historyCode) ?? null,
    onShowHistory: (code: string) => { setEditing(null); setHistoryCode(code); },
    onHideHistory: () => setHistoryCode(null),
  };
}
