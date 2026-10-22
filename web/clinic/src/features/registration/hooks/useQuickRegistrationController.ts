import { useEffect, useMemo, useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery } from '@tanstack/react-query';
import type { Country } from '@medvya/i18n';
import {
  duplicateQuery, emptyQuickForm, formField, quickSchema, resolves, toRegisterRequest,
  type DuplicateDecision, type DuplicateQuery, type DuplicateResolution, type QuickForm, type RegistrationResult,
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
 * Quick registration (MDV-P1-015) with the duplicate warning while typing and the decision before save (MDV-P1-016).
 * Save stays blocked until the desk has decided about every possible match; the server enforces the same rule.
 */
export function useQuickRegistrationController(country: Country) {
  const form = useForm<QuickForm>({ resolver: zodResolver(quickSchema(country)), defaultValues: emptyQuickForm, mode: 'onTouched' });
  const values = useWatch({ control: form.control }) as QuickForm;
  const query = duplicateQuery(values, country);
  const key = JSON.stringify(query);
  const debouncedKey = useDebounced(key, duplicateCheckDelayMs);
  const debounced = useMemo(() => JSON.parse(debouncedKey) as DuplicateQuery | null, [debouncedKey]);

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

  const save = useMutation({
    mutationFn: (v: QuickForm) => registerPatient(toRegisterRequest(v, country, current.length > 0 ? resolution : null)),
    onError: error => {
      if (!(error instanceof ApiError) || error.status !== 400) return;
      for (const e of error.errors) {
        if (e.field === 'duplicates') void duplicates.refetch(); // a match appeared since the last check: show it
        const field = formField(e.field);
        if (field) form.setError(field, { message: e.code });
      }
    },
  });

  const failure = save.error instanceof ApiError ? save.error : save.error ? new ApiError(0) : null;
  return {
    form,
    country,
    ageInstead: country === 'IN' && values.ageInstead,
    checking: debounced !== null && (duplicates.isFetching || debouncedKey !== key),
    /** The check could not run; saving still re-checks on the server, which refuses an unresolved match. */
    checkFailed: duplicates.isError,
    candidates: current,
    resolution: resolved && current.length > 0 ? resolution!.decision : null,
    onResolve: (decision: DuplicateDecision) => setResolution({ decision, candidateIds: current.map(c => c.patientId) }),
    onReconsider: () => setResolution(null),
    canSave: resolved && debouncedKey === key && !duplicates.isFetching,
    saving: save.isPending,
    onSubmit: form.handleSubmit(v => save.mutate(v)),
    failure: failure && failure.status !== 400 ? (failure.status === 403 ? 'forbidden' as const : 'error' as const) : null,
    duplicateUnresolved: failure?.errors.some(e => e.field === 'duplicates') ?? false,
    result: save.data ?? null as RegistrationResult | null,
    registeredName: save.variables?.fullName.trim() ?? '',
    onRegisterAnother: () => { save.reset(); setResolution(null); form.reset(emptyQuickForm); },
  };
}
