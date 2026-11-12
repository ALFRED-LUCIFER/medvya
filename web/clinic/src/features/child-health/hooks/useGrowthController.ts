import { useState } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { ApiError } from '../../schedule/services/scheduleApi';
import { emptyGrowth, growthBody, growthProblemOf, growthSchema, type Growth, type GrowthDraft, type GrowthIndicator, type GrowthProblem } from '../domain/growth';
import { fetchGrowth, recordGrowth } from '../services/childHealthApi';

/**
 * A child's growth on Patient 360 (MDV-P2-035). Asked for only for a child; every open is audited by the server, so it is not retried or
 * refetched on focus. A record answers with the whole card, which replaces the cached one.
 */
export function useGrowthController(patientId: string, enabled: boolean) {
  const queryClient = useQueryClient();
  const key = ['growth', patientId];
  const query = useQuery({ queryKey: key, queryFn: () => fetchGrowth(patientId), enabled, retry: false, refetchOnWindowFocus: false });
  const form = useForm<GrowthDraft>({ resolver: zodResolver(growthSchema), defaultValues: emptyGrowth });
  const [indicator, setIndicator] = useState<GrowthIndicator>('WeightForAge');
  const [problem, setProblem] = useState<GrowthProblem | null>(null);
  const save = useMutation({
    mutationFn: (d: GrowthDraft) => recordGrowth(patientId, growthBody(d)),
    onSuccess: (view: Growth) => { setProblem(null); queryClient.setQueryData(key, view); form.reset(emptyGrowth); },
    onError: (e: unknown) => setProblem(e instanceof ApiError ? growthProblemOf(e.status, e.code) : 'failed'),
  });
  return {
    view: query.data ?? null,
    loading: enabled && query.isPending,
    failed: query.isError,
    form, problem, indicator, recording: save.isPending,
    onIndicator: setIndicator,
    onRecord: form.handleSubmit(d => save.mutate(d)),
  };
}
