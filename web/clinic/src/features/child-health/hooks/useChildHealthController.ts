import { useState } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { ApiError } from '../../schedule/services/scheduleApi';
import { doseBody, doseSchema, emptyDose, problemOf, type ChildHealth, type DoseDraft, type MilestoneAnswer, type MilestoneAnswerer, type MilestoneKey, type Problem } from '../domain/childHealth';
import { answerMilestone, fetchChildHealth, recordDose } from '../services/childHealthApi';

/**
 * A child's immunisations and milestones on Patient 360 (MDV-P2-036). Only asked for a child (<paramref name="enabled"/>); every open is
 * audited by the server, so it is not retried or refetched on focus. Every write answers with the whole card, which replaces the cached one.
 */
export function useChildHealthController(patientId: string, enabled: boolean) {
  const queryClient = useQueryClient();
  const key = ['child-health', patientId];
  const query = useQuery({ queryKey: key, queryFn: () => fetchChildHealth(patientId), enabled, retry: false, refetchOnWindowFocus: false });
  const form = useForm<DoseDraft>({ resolver: zodResolver(doseSchema), defaultValues: emptyDose });
  const [answeredBy, setAnsweredBy] = useState<MilestoneAnswerer>('Parent');
  const [problem, setProblem] = useState<Problem | null>(null);
  const done = (view: ChildHealth) => {
    setProblem(null);
    queryClient.setQueryData(key, view);
  };
  const fail = (e: unknown) => setProblem(e instanceof ApiError ? problemOf(e.status, e.code) : 'failed');
  const dose = useMutation({ mutationFn: (d: DoseDraft) => recordDose(patientId, doseBody(d)), onSuccess: v => { done(v); form.reset(emptyDose); }, onError: fail });
  const answer = useMutation({
    mutationFn: ({ milestone, value }: { milestone: MilestoneKey; value: MilestoneAnswer }) =>
      answerMilestone(patientId, { band: query.data?.milestones?.key ?? '', milestone, answer: value, answeredBy }),
    onSuccess: done, onError: fail,
  });
  return {
    view: query.data ?? null,
    loading: enabled && query.isPending,
    failed: query.isError,
    form, problem, answeredBy,
    recording: dose.isPending, answering: answer.isPending,
    onRecord: form.handleSubmit(d => dose.mutate(d)),
    onAnsweredBy: setAnsweredBy,
    onAnswer: (milestone: MilestoneKey, value: MilestoneAnswer) => answer.mutate({ milestone, value }),
  };
}
