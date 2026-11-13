import { useEffect, useMemo, useRef, useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery } from '@tanstack/react-query';
import type { Country } from '@medvya/i18n';
import {
  duplicateQuery, emptyForm, formField, isMinor, isoDay, messageFor, quickFields, registrationSchema, resolves, stepFields, stepOf, steps, toRegisterRequest,
  type DuplicateDecision, type DuplicateQuery, type DuplicateResolution, type FormField, type RegisterRequest, type RegistrationForm,
  type RegistrationResult, type Step,
} from '../domain/registration';

type FormStep = Exclude<Step, 'review'>;
import { missingParts } from '../../patients/domain/directory';
import { forgetKey, type HeldKey, keyFor } from '../../api/services/idempotencyKey';
import { ApiError } from '../../patients/services/patientsApi';
import { checkDuplicates, registerPatient } from '../services/registrationApi';
import { patientDataGcTime } from '../../patients/hooks/usePatientDirectoryController';

/** Typing pauses this long before the duplicate check runs; each check is audited and rate-limited. */
export const duplicateCheckDelayMs = 500;

/**
 * An interrupted registration is kept in memory (never browser storage, AGENTS.md 1.2) and restored when the desk comes back to this screen
 * in the same facility. shortcut: no leave-page prompt, because the app uses BrowserRouter (no blocker); the draft is what protects the typing.
 * Move to a data router if a prompt is wanted.
 */
type Draft = { scope: string; values: RegistrationForm; step: Step; reached: number };
let draft: Draft | null = null;
const draftFor = (scope: string) => (draft?.scope === scope ? draft : null);

function useDebounced<T>(value: T, ms: number): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), ms);
    return () => clearTimeout(timer);
  }, [value, ms]);
  return debounced;
}

/**
 * Patient registration (P-02): full mode in steps (MDV-P1-013 India, MDV-P1-014 UAE) or quick mode on one screen (MDV-P1-015),
 * with the duplicate warning while typing and the decision before save (MDV-P1-016). Switching mode keeps what was typed.
 * Save stays blocked until the desk has decided about every possible match; the server enforces the same rule.
 */
export function useRegistrationController(country: Country, quick: boolean, scope = '') {
  // The desk's calendar day for the expired-ID warning, the preview age and the under-18 guardian rule; the server decides
  // with the facility's day.
  const [today] = useState(() => isoDay(new Date()));
  const schema = useMemo(() => registrationSchema(country, quick, today), [country, quick, today]);
  const [restored] = useState(() => draftFor(scope));
  const form = useForm<RegistrationForm>({
    resolver: zodResolver(schema), defaultValues: restored?.values ?? emptyForm, mode: 'onTouched', shouldFocusError: false, // the error summary takes focus
  });
  const values = useWatch({ control: form.control }) as RegistrationForm;
  const query = duplicateQuery(values, country, quick);
  const key = JSON.stringify(query);
  const debouncedKey = useDebounced(key, duplicateCheckDelayMs);
  const debounced = useMemo(() => JSON.parse(debouncedKey) as DuplicateQuery | null, [debouncedKey]);
  const [step, setStep] = useState<Step>(restored?.step ?? 'identity');
  const [reached, setReached] = useState(restored?.reached ?? 0);
  // Each failed submit or Continue: the error summary takes focus once and lists the errors of the step it happened on (null: quick mode).
  const [failed, setFailed] = useState<{ count: number; step: FormStep | null }>({ count: 0, step: null });
  const fail = (s: FormStep | null) => setFailed(f => ({ count: f.count + 1, step: s }));

  const duplicates = useQuery({
    queryKey: ['patients', 'duplicates', debounced],
    queryFn: () => checkDuplicates(debounced!),
    enabled: debounced !== null,
    gcTime: patientDataGcTime,
  });
  // Only matches for what is on screen now count; while the check catches up, the previous answer is not trusted.
  const current = debounced !== null && debouncedKey === key ? duplicates.data ?? [] : [];
  const [resolution, setResolution] = useState<DuplicateResolution | null>(null);
  const resolved = resolves(resolution, current);

  const goTo = (s: Step) => {
    setStep(s);
    setReached(r => Math.max(r, steps.indexOf(s)));
  };
  const firstErrorStep = (fields: FormField[]): FormStep => {
    const first = steps.find(s => fields.some(f => stepOf(f) === s));
    return first && first !== 'review' ? first : 'identity';
  };
  const failAt = (fields: FormField[]) => {
    const s = quick ? null : firstErrorStep(fields);
    if (s) goTo(s);
    fail(s);
  };

  const saveKey: HeldKey = useRef(null); // one key per submission, kept across retries and taps
  const save = useMutation({
    mutationFn: (request: RegisterRequest) => registerPatient(request, keyFor(saveKey, request)),
    onSuccess: () => forgetKey(saveKey),
    onError: (error, request) => {
      if (!(error instanceof ApiError) || error.status !== 400) return;
      const fields: FormField[] = [];
      for (const e of error.errors) {
        if (e.field === 'duplicates') void duplicates.refetch(); // a match appeared since the last check: show it
        const field = formField(e.field, request, country);
        if (field) { form.setError(field, { message: messageFor(field, e.code) }); fields.push(field); }
      }
      if (fields.length > 0) failAt(fields);
    },
  });
  const submit = form.handleSubmit(
    v => save.mutate(toRegisterRequest(v, country, quick, current.length > 0 ? resolution : null)),
    errors => failAt(Object.keys(errors) as FormField[]),
  );

  // Keep the draft when leaving the screen; a saved or cleared form leaves none.
  const latest = useRef({ step, reached, saved: false });
  useEffect(() => { latest.current = { step, reached, saved: save.isSuccess }; });
  useEffect(() => () => {
    const { step: s, reached: r, saved } = latest.current;
    const values = form.getValues();
    draft = saved || JSON.stringify(values) === JSON.stringify(emptyForm) ? null : { scope, values, step: s, reached: r };
  }, [form, scope]);

  const failure = save.error instanceof ApiError ? save.error : save.error ? new ApiError(0) : null;
  const index = steps.indexOf(step);
  const errors = form.formState.errors;
  const summaryShown = failed.count > 0 && (quick || failed.step === step);
  return {
    form,
    /** A draft from an earlier visit to this screen was put back. */
    restored: restored !== null,
    country,
    quick,
    values,
    today,
    ageInstead: country === 'IN' && values.ageInstead,
    /** Under 18 (null while unknown): the guardian is then required, in quick mode too. */
    minor: isMinor(values, country, today),
    step: quick ? null : step,
    /** Steps up to here can be opened from the stepper (later ones only once reached with Continue). */
    reachedIndex: reached,
    onGoTo: (s: Step) => { if (steps.indexOf(s) <= reached) setStep(s); },
    onBack: () => { if (index > 0) setStep(steps[index - 1]!); },
    /** Validates this step's fields only, then moves on. */
    onContinue: async () => {
      if (step === 'review') return;
      if (await form.trigger([...stepFields[step]])) goTo(steps[index + 1]!);
      else fail(step);
    },
    /** After a failed submit: the fields in error on this screen, in screen order; focusKey changes once per failure. */
    errorSummary: {
      focusKey: failed.count,
      fields: summaryShown ? (quick ? quickFields : failed.step ? stepFields[failed.step] : []).filter(f => errors[f]) : [],
    },
    checking: debounced !== null && (duplicates.isFetching || debouncedKey !== key),
    /** The check could not run; saving still re-checks on the server, which refuses an unresolved match. */
    checkFailed: duplicates.isError,
    candidates: current,
    resolution: resolved && current.length > 0 ? resolution!.decision : null,
    onResolve: (decision: DuplicateDecision) => setResolution({ decision, candidateIds: current.map(c => c.patientId) }),
    onReconsider: () => setResolution(null),
    canSave: resolved && debouncedKey === key && !duplicates.isFetching,
    saving: save.isPending,
    onSubmit: submit,
    failure: failure && failure.status !== 400 ? (failure.status === 403 ? 'forbidden' as const : 'error' as const) : null,
    duplicateUnresolved: failure?.errors.some(e => e.field === 'duplicates') ?? false,
    consentsStale: failure?.errors.some(e => e.field === 'consents.policyVersion') ?? false,
    result: save.data ?? null as RegistrationResult | null,
    registeredName: save.variables?.fullName ?? '',
    missing: save.variables ? missingParts(save.variables) : [],
    onRegisterAnother: () => { draft = null; save.reset(); setResolution(null); form.reset(emptyForm); setStep('identity'); setReached(0); setFailed({ count: 0, step: null }); },
  };
}

export type RegistrationController = ReturnType<typeof useRegistrationController>;
