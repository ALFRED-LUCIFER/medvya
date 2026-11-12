import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { fetchNote, saveNote } from '../../encounters/services/noteApi';
import { addOrder } from '../../orders/services/ordersApi';
import { createReferral } from '../../referrals/services/referralsApi';
import { ApiError } from '../../schedule/services/scheduleApi';
import {
  checkFormSchema, checkKindOf, emptyGoal, gapKey, goalFormSchema, progressFormSchema, targetFormSchema, toGoalBody, toLimit, withPlanLine,
  type CareGap, type CheckForm, type GapOutcome, type GoalForm, type ProgressForm, type TargetCode, type TargetForm,
} from '../domain/chronicCare';
import { addCareGoal, fetchChronicCare, recordCareCheck, setCareTarget, updateCareGoal } from '../services/chronicCareApi';

const status = (e: unknown) => (e instanceof ApiError ? e.status : null);
const todayIn = (zone: string) => new Intl.DateTimeFormat('en-CA', { timeZone: zone }).format(new Date());

/**
 * The chart's chronic care cards (MDV-P2-038): targets, care gaps due today and goals. The read is audited, so nothing refetches on focus.
 * A gap's action goes on the open consultation (MDV-P2-039's rule): an order or a referral as a draft there, anything else as a line in the
 * note's plan; with none open it says so and calls nothing. The server checks every write.
 */
export function useChronicCareController(patientId: string, openEncounterId: string | null) {
  const queryClient = useQueryClient();
  const key = ['chronic-care', patientId];
  const care = useQuery({ queryKey: key, queryFn: () => fetchChronicCare(patientId), retry: false, refetchOnWindowFocus: false });
  const refresh = () => void queryClient.invalidateQueries({ queryKey: key });
  const zone = care.data?.timeZone ?? 'UTC';

  const [editing, setEditing] = useState<TargetCode | null>(null);
  const targetForm = useForm<TargetForm>({ resolver: zodResolver(targetFormSchema), defaultValues: { limit: '' } });
  const target = useMutation({
    mutationFn: (v: { code: TargetCode; limit: number }) => setCareTarget(patientId, v.code, v.limit),
    onSuccess: () => { setEditing(null); refresh(); },
  });

  const [checking, setChecking] = useState<string | null>(null);
  const checkForm = useForm<CheckForm>({ resolver: zodResolver(checkFormSchema), defaultValues: { doneOn: '' } });
  const check = useMutation({
    mutationFn: (v: { kind: string; doneOn: string }) => recordCareCheck(patientId, v.kind, v.doneOn),
    onSuccess: () => { setChecking(null); refresh(); },
  });

  const [outcome, setOutcome] = useState<{ key: string; outcome: GapOutcome } | null>(null);
  const action = useMutation({
    mutationFn: async (g: CareGap): Promise<GapOutcome> => {
      if (!openEncounterId) return 'noConsult';
      if (g.action === 'Order') await addOrder(openEncounterId, { kind: 'Lab', name: g.item, note: '' });
      else if (g.action === 'Refer') await createReferral(openEncounterId, { provider: g.referTo ?? g.item, urgency: 'Routine', reason: g.item, sections: [] });
      else {
        const note = await fetchNote(openEncounterId);
        if (note.status === 'Signed') return 'signed';
        await saveNote(openEncounterId, { templateKey: note.templateKey, sections: { ...note.sections, plan: withPlanLine(note.sections.plan, g.item) }, recordingConsent: note.recordingConsent ?? null }, note.version);
      }
      return 'added';
    },
    onSuccess: (result, g) => setOutcome({ key: gapKey(g), outcome: result }),
    // Already on the consultation is what was asked for; another clash (a save in between, an ended visit) is said so, never retried blindly.
    onError: (e, g) => setOutcome({ key: gapKey(g), outcome: e instanceof ApiError && e.code === 'already_listed' ? 'added' : status(e) === 409 ? 'conflict' : 'failed' }),
  });

  const [adding, setAdding] = useState(false);
  const goalForm = useForm<GoalForm>({ resolver: zodResolver(goalFormSchema), defaultValues: emptyGoal });
  const goal = useMutation({
    mutationFn: (v: GoalForm) => addCareGoal(patientId, toGoalBody(v)),
    onSuccess: () => { setAdding(false); goalForm.reset(emptyGoal); refresh(); },
  });
  const [moving, setMoving] = useState<string | null>(null);
  const progressForm = useForm<ProgressForm>({ resolver: zodResolver(progressFormSchema), defaultValues: { progress: '' } });
  const progress = useMutation({
    mutationFn: (v: { id: string; progress: number }) => updateCareGoal(patientId, v.id, v.progress),
    onSuccess: () => { setMoving(null); refresh(); },
  });

  return {
    care: care.data ?? null,
    loading: care.isPending,
    failed: care.isError && status(care.error) !== 403,
    hidden: status(care.error) === 403 || status(care.error) === 404,
    // targets
    editing, targetForm, savingTarget: target.isPending, targetFailed: target.isError,
    onEditTarget: (code: TargetCode, limit: number) => { target.reset(); targetForm.reset({ limit: String(limit) }); setEditing(code); },
    onCancelTarget: () => setEditing(null),
    onSubmitTarget: targetForm.handleSubmit(v => { if (editing) target.mutate({ code: editing, limit: toLimit(v) }); }),
    // gaps
    outcome, acting: action.isPending ? action.variables : null,
    onAct: (g: CareGap) => { setOutcome(null); action.mutate(g); },
    checking, checkForm, savingCheck: check.isPending, checkFailed: check.isError,
    onCheck: (g: CareGap) => { check.reset(); checkForm.reset({ doneOn: todayIn(zone) }); setChecking(gapKey(g)); },
    onCancelCheck: () => setChecking(null),
    onSubmitCheck: (g: CareGap) => checkForm.handleSubmit(v => { const kind = checkKindOf(g); if (kind) check.mutate({ kind, doneOn: v.doneOn }); }),
    // goals
    adding, goalForm, savingGoal: goal.isPending, goalFailed: goal.isError,
    onAddGoal: () => { goal.reset(); setAdding(true); },
    onCancelGoal: () => { setAdding(false); goalForm.reset(emptyGoal); },
    onSubmitGoal: goalForm.handleSubmit(v => goal.mutate(v)),
    moving, progressForm, savingProgress: progress.isPending, progressFailed: progress.isError,
    onMove: (id: string, current: number) => { progress.reset(); progressForm.reset({ progress: String(current) }); setMoving(id); },
    onCancelMove: () => setMoving(null),
    onSubmitMove: progressForm.handleSubmit(v => { if (moving) progress.mutate({ id: moving, progress: Number(v.progress) }); }),
  };
}
