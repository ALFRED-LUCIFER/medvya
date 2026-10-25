import { useEffect, useMemo, useState } from 'react';
import { useForm, useWatch, type FieldErrors } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery } from '@tanstack/react-query';
import type { Country } from '@medvya/i18n';
import {
  duplicateQuery, emptyForm, formField, isoDay, messageFor, registrationSchema, resolves, stepFields, stepOf, steps, toRegisterRequest,
  type DuplicateDecision, type DuplicateQuery, type DuplicateResolution, type FormField, type RegisterRequest, type RegistrationForm,
  type RegistrationResult, type Step,
} from '../domain/registration';
import { ApiError } from '../../patients/services/patientsApi';
import { checkDuplicates, registerPatient } from '../services/registrationApi';
import { noRetryOnAnswers, patientDataGcTime } from '../../patients/hooks/usePatientDirectoryController';

/** Typing pauses this long before the duplicate check runs; each check is audited and rate-limited. */
export const duplicateCheckDelayMs = 500;

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
export function useRegistrationController(country: Country, quick: boolean) {
  const schema = useMemo(() => registrationSchema(country, quick), [country, quick]);
  const form = useForm<RegistrationForm>({ resolver: zodResolver(schema), defaultValues: emptyForm, mode: 'onTouched' });
  const values = useWatch({ control: form.control }) as RegistrationForm;
  const query = duplicateQuery(values, country, quick);
  const key = JSON.stringify(query);
  const debouncedKey = useDebounced(key, duplicateCheckDelayMs);
  const debounced = useMemo(() => JSON.parse(debouncedKey) as DuplicateQuery | null, [debouncedKey]);
  const [step, setStep] = useState<Step>('identity');
  const [reached, setReached] = useState(0);
  // The desk's calendar day for the expired-ID warning and the preview age; the server decides with the facility's day.
  const [today] = useState(() => isoDay(new Date()));

  const duplicates = useQuery({
    queryKey: ['patients', 'duplicates', debounced],
    queryFn: () => checkDuplicates(debounced!),
    enabled: debounced !== null,
    gcTime: patientDataGcTime,
    retry: noRetryOnAnswers,
  });
  // Only matches for what is on screen now count; while the check catches up, the previous answer is not trusted.
  const current = debounced !== null && debouncedKey === key ? duplicates.data ?? [] : [];
  const [resolution, setResolution] = useState<DuplicateResolution | null>(null);
  const resolved = resolves(resolution, current);

  const goTo = (s: Step) => {
    setStep(s);
    setReached(r => Math.max(r, steps.indexOf(s)));
  };
  const firstErrorStep = (errors: FieldErrors<RegistrationForm>) => {
    const fields = Object.keys(errors) as FormField[];
    return steps.find(s => fields.some(f => stepOf(f) === s)) ?? 'identity';
  };

  const save = useMutation({
    mutationFn: (request: RegisterRequest) => registerPatient(request),
    onError: (error, request) => {
      if (!(error instanceof ApiError) || error.status !== 400) return;
      const fields: FormField[] = [];
      for (const e of error.errors) {
        if (e.field === 'duplicates') void duplicates.refetch(); // a match appeared since the last check: show it
        const field = formField(e.field, request, country);
        if (field) { form.setError(field, { message: messageFor(field, e.code) }); fields.push(field); }
      }
      if (!quick && fields.length > 0) goTo(steps.find(s => fields.some(f => stepOf(f) === s)) ?? 'identity');
    },
  });
  const submit = form.handleSubmit(
    v => save.mutate(toRegisterRequest(v, country, quick, current.length > 0 ? resolution : null)),
    errors => { if (!quick) goTo(firstErrorStep(errors)); },
  );

  const failure = save.error instanceof ApiError ? save.error : save.error ? new ApiError(0) : null;
  const index = steps.indexOf(step);
  return {
    form,
    country,
    quick,
    values,
    today,
    ageInstead: country === 'IN' && values.ageInstead,
    step: quick ? null : step,
    /** Steps up to here can be opened from the stepper (later ones only once reached with Continue). */
    reachedIndex: reached,
    onGoTo: (s: Step) => { if (steps.indexOf(s) <= reached) setStep(s); },
    onBack: () => { if (index > 0) setStep(steps[index - 1]!); },
    /** Validates this step's fields only, then moves on. */
    onContinue: async () => {
      if (step === 'review') return;
      if (await form.trigger([...stepFields[step]])) goTo(steps[index + 1]!);
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
    result: save.data ?? null as RegistrationResult | null,
    registeredName: save.variables?.fullName ?? '',
    onRegisterAnother: () => { save.reset(); setResolution(null); form.reset(emptyForm); setStep('identity'); setReached(0); },
  };
}

export type RegistrationController = ReturnType<typeof useRegistrationController>;
