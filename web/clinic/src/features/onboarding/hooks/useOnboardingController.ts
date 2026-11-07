import { useCallback, useEffect, useRef, useState } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm, useWatch } from 'react-hook-form';
import type { Country } from '@medvya/i18n';
import {
  addMember, emptyForm, formFieldOf, stepFields, stepOfPath, steps, stepSchema, toData, toForm,
  type Draft, type FinishResult, type WizardForm,
} from '../domain/onboarding';
import { fetchDraft, finishOnboarding, saveDraft, startDraft, ValidationError } from '../services/onboardingApi';

const key = ['onboarding'];
const autosaveMs = 1200;

export type SaveState = 'idle' | 'saving' | 'saved' | 'failed';

/**
 * F-04 set-up wizard (MDV-P1-004, 6 Nov steps MDV-P1-061). The draft lives on the server (resume after signing in again, on any
 * device); this keeps the form, autosaves a moment after typing stops, and on Continue asks the server to check the step first.
 * Finishing shows the ready screen; the person then opens the app (or the schedule to book a first patient).
 */
export function useOnboardingController(initialCountry: Country, onFinished: (result: FinishResult, to?: string) => void, onLater: () => void) {
  const queryClient = useQueryClient();
  const draft = useQuery({ queryKey: key, queryFn: fetchDraft, retry: false, staleTime: Infinity });
  const [step, setStep] = useState(1);
  const stepNow = useRef(step);
  useEffect(() => { stepNow.current = step; }, [step]);
  // The step on screen decides what is required, so another step's empty fields never block this one.
  const form = useForm<WizardForm>({ resolver: (v, c, o) => zodResolver(stepSchema(stepNow.current))(v, c, o), defaultValues: emptyForm(initialCountry), mode: 'onChange' });
  const values = useWatch({ control: form.control }) as WizardForm;
  const [country, setCountry] = useState<Country>(initialCountry);
  const [saveState, setSaveState] = useState<SaveState>('idle');
  const [problem, setProblem] = useState<'check' | 'finish' | 'later' | null>(null);
  const [done, setDone] = useState<FinishResult | null>(null);
  const saved = useRef<string | null>(null); // the step and data the server holds, so nothing is saved twice
  const snapshot = (at: number, f: WizardForm) => JSON.stringify({ step: at, data: toData(f) });
  const data = draft.data;

  // The saved draft fills the form once (not on every refetch, which would overwrite what is being typed).
  useEffect(() => {
    if (!data || saved.current !== null) return;
    const f = toForm(data.data, data.country);
    form.reset(f);
    const at = steps.includes(data.step) ? data.step : 1; // a draft from the earlier six steps starts again at the first
    saved.current = snapshot(at, f);
    setStep(at);
  }, [data, form]);

  const save = useMutation({
    mutationFn: (v: { step: number; form: WizardForm; validate?: number }) => saveDraft(v.step, toData(v.form), v.validate),
  });

  const start = useMutation({
    mutationFn: startDraft,
    onSuccess: (d: Draft) => queryClient.setQueryData(key, d),
  });

  // Autosave: shortly after the last change, keep what was typed without checking it (a half-finished step is fine).
  useEffect(() => {
    if (!data || done || saved.current === null || snapshot(step, values) === saved.current) return;
    const id = setTimeout(() => {
      setSaveState('saving');
      save.mutate({ step, form: values }, {
        onSuccess: () => { saved.current = snapshot(step, values); setSaveState('saved'); },
        onError: () => setSaveState('failed'),
      });
    }, autosaveMs);
    return () => clearTimeout(id);
  }, [values, step, data, done, save]);

  const index = Math.max(0, steps.indexOf(step));
  const last = index === steps.length - 1;

  const mapErrors = useCallback((e: ValidationError) => {
    let first: number | null = null;
    for (const [path, codes] of Object.entries(e.errors)) {
      const field = formFieldOf(path);
      if (field) form.setError(field, { message: codes[0] ?? 'format' });
      first = Math.min(first ?? Infinity, stepOfPath(path));
    }
    return first;
  }, [form]);

  const go = useCallback((to: number) => { setProblem(null); setStep(to); }, []);

  const next = async () => {
    setProblem(null);
    if (!(await form.trigger(stepFields[step]))) { setProblem('check'); return; }
    try {
      const at = last ? step : steps[index + 1]!;
      await save.mutateAsync({ step: at, form: form.getValues(), validate: step });
      saved.current = snapshot(at, form.getValues());
      if (!last) { setStep(at); return; }
      setDone(await finishOnboarding());
    } catch (e) {
      if (e instanceof ValidationError) {
        // Finishing re-checks every step; if an earlier one is wrong, go back to it.
        const first = mapErrors(e);
        if (first !== null && first !== step) setStep(first);
        setProblem('check');
      } else setProblem('finish');
    }
  };

  return {
    loading: draft.isPending,
    loadFailed: draft.isError,
    retry: () => void draft.refetch(),
    started: Boolean(data),
    country: data?.country ?? country,
    chooseCountry: setCountry,
    start: () => start.mutate(country),
    starting: start.isPending,
    startFailed: start.isError,
    rules: data?.rules ?? null,
    form,
    step, steps, index, last,
    saveState,
    problem,
    working: save.isPending,
    done,
    next,
    /** "Skip for now": the step is left empty (nothing invited, no patients choice) and the wizard moves on. */
    skip: () => {
      if (step === 3) { form.setValue('team', []); form.setValue('inviteEmail', ''); }
      if (step === 4) form.setValue('patients', '');
      void next();
    },
    back: () => { if (index > 0) go(steps[index - 1]!); },
    goTo: go,
    invite: () => {
      const r = addMember(form.getValues('team'), form.getValues('inviteEmail'), form.getValues('inviteRole'));
      if ('error' in r) { form.setError('inviteEmail', { message: r.error }); return; }
      form.setValue('team', r.team, { shouldDirty: true });
      form.setValue('inviteEmail', '');
      form.clearErrors(['inviteEmail', 'team']);
    },
    removeMember: (i: number) => { form.setValue('team', form.getValues('team').filter((_, j) => j !== i), { shouldDirty: true }); form.clearErrors('team'); },
    /** "Finish later": what is on screen is saved now, then the person signs out; signing in again resumes here. */
    later: async () => {
      setProblem(null);
      try { await save.mutateAsync({ step, form: form.getValues() }); onLater(); } catch { setProblem('later'); }
    },
    open: (to?: string) => { if (done) onFinished(done, to); },
  };
}
