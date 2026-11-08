import { useEffect, useState } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { ApiError } from '../../schedule/services/scheduleApi';
import { escalateSchema, formOf, intakeFormSchema, intakeProblem, invalidKind, toRecord, type IntakeForm, type IntakeProblem, type IntakeScreen, type IntakeView, type VitalKind } from '../domain/intake';
import { escalateIntake, fetchIntake, handOffIntake, keepVital, saveIntake } from '../services/intakeApi';

/**
 * One nurse intake (N-01, MDV-P2-007): the form for the readings and chief complaint, the nurse's decisions on flagged readings, the
 * escalation with its reason, and the handoff. The server answers every call with the whole intake (flags, blockers), which replaces the
 * cached one; the screen never works out a flag or a blocker itself.
 */
export function useIntakeController(appointmentId: string, onHandedOff: () => void) {
  const queryClient = useQueryClient();
  const key = ['intake', appointmentId];
  const query = useQuery({ queryKey: key, queryFn: () => fetchIntake(appointmentId), retry: false });
  const form = useForm<IntakeForm>({ resolver: zodResolver(intakeFormSchema), defaultValues: formOf(null) });
  const escalation = useForm({ resolver: zodResolver(escalateSchema), defaultValues: { reason: '' } });
  const [escalating, setEscalating] = useState(false);
  const [problem, setProblem] = useState<IntakeProblem | null>(null);
  const [badKind, setBadKind] = useState<VitalKind | null>(null);

  // The form starts from what is saved, once: later answers must not overwrite what the nurse is typing.
  const loaded = query.data !== undefined;
  useEffect(() => {
    if (loaded) form.reset(formOf(queryClient.getQueryData<IntakeScreen>(key)?.intake ?? null));
    // eslint-disable-next-line react-hooks/exhaustive-deps -- once per loaded intake
  }, [loaded]);

  const take = (intake: IntakeView) => {
    setProblem(null);
    setBadKind(null);
    queryClient.setQueryData<IntakeScreen>(key, old => old && { ...old, intake });
  };
  const fail = (e: unknown) => {
    const p = e instanceof ApiError ? intakeProblem(e.status, e.code) : ({ kind: 'failed' } as const);
    setProblem(p);
    if (p.kind === 'conflict') void queryClient.invalidateQueries({ queryKey: key }); // it moved on elsewhere: show where it is now
  };

  const sentReadings = (v: IntakeForm) => toRecord(v).vitals;
  const save = useMutation({
    mutationFn: (v: IntakeForm) => saveIntake(appointmentId, toRecord(v)),
    onSuccess: intake => { take(intake); form.reset(formOf(intake)); }, // saved: nothing left unsent, so the handoff may go on these readings
    onError: (e, v) => {
      fail(e);
      if (e instanceof ApiError) {
        const p = intakeProblem(e.status, e.code);
        setBadKind(p.kind === 'invalid' ? invalidKind(p.field, sentReadings(v)) : null);
      }
    },
  });
  const keep = useMutation({ mutationFn: (kind: VitalKind) => keepVital(appointmentId, kind), onSuccess: take, onError: fail });
  const escalate = useMutation({
    mutationFn: (reason: string) => escalateIntake(appointmentId, reason),
    onSuccess: intake => { take(intake); setEscalating(false); escalation.reset(); },
    onError: fail,
  });
  const handOff = useMutation({ mutationFn: () => handOffIntake(appointmentId), onSuccess: intake => { take(intake); onHandedOff(); }, onError: fail });

  const error = query.error instanceof ApiError ? query.error : null;
  return {
    screen: query.data ?? null,
    loading: query.isPending,
    notFound: error?.status === 404,
    refused: error?.status === 403,
    failed: query.isError,
    form,
    escalationForm: escalation,
    problem,
    /** The reading the server called impossible (its field), so the screen can mark it. */
    badKind,
    saving: save.isPending,
    keeping: keep.isPending ? keep.variables : null,
    escalating,
    sendingEscalation: escalate.isPending,
    handingOff: handOff.isPending,
    onSave: form.handleSubmit(v => save.mutate(v)),
    onKeep: (kind: VitalKind) => keep.mutate(kind),
    onRemeasure: (kind: VitalKind) => form.setFocus(kind, { shouldSelect: true }),
    /** Typed since the last save: the handoff goes on the saved readings, so it waits for the save. */
    dirty: form.formState.isDirty,
    onToggleEscalate: () => setEscalating(e => !e),
    onEscalate: escalation.handleSubmit(v => escalate.mutate(v.reason)),
    onHandOff: () => handOff.mutate(),
  };
}
