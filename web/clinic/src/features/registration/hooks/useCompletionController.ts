import { useMemo } from 'react';
import { useForm, useWatch, type Resolver } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { Country } from '@medvya/i18n';
import { completionSchema, emptyForm, formField, messageFor, toCompletionRequest, type CompletionRequest, type RegistrationForm } from '../domain/registration';
import { ApiError, fetchPatient } from '../../patients/services/patientsApi';
import { completeRegistration } from '../services/registrationApi';

/**
 * "Complete registration" (MDV-P1-015): adds the address and/or payer a quick registration left out. The server closes the
 * desk's completion task (status Complete) once both exist, and answers 404 for any patient outside the active facility.
 */
export function useCompletionController(patientId: string, country: Country) {
  // The completion schema checks only address and payer, so it parses those fields of the shared registration form;
  // the cast states that, and toCompletionRequest sends nothing else.
  const resolver = useMemo(() => zodResolver(completionSchema(country)) as unknown as Resolver<RegistrationForm>, [country]);
  const form = useForm<RegistrationForm>({ resolver, defaultValues: emptyForm, mode: 'onTouched' });
  const values = useWatch({ control: form.control }) as RegistrationForm;
  const queryClient = useQueryClient();

  // The record is read for its version only (#889): the save carries it and is refused if someone else changed the record since.
  // gcTime 0: never a cached version from an earlier visit.
  const record = useQuery({ queryKey: ['completion', patientId], queryFn: () => fetchPatient(patientId), gcTime: 0 });

  const save = useMutation({
    mutationFn: (request: CompletionRequest) => completeRegistration(patientId, request, record.data!.version),
    // The record and directory show the status: read them again (each an audited read) when next shown.
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['patients'] }),
    onError: error => {
      if (!(error instanceof ApiError) || error.status !== 400) return;
      for (const e of error.errors) {
        const field = formField(e.field, { identifiers: [] }, country);
        if (field) form.setError(field, { message: messageFor(field, e.code) });
      }
    },
  });

  const failure = save.error instanceof ApiError ? save.error.status : record.error instanceof ApiError ? record.error.status : save.error || record.error ? 0 : null;
  return {
    form,
    country,
    values,
    loading: record.isPending,
    saving: save.isPending,
    onSubmit: form.handleSubmit(v => { if (record.data) save.mutate(toCompletionRequest(v, country)); }),
    // After "changed": drop what was typed and read the record again, so the next save starts from what the other person saved.
    onReload: () => { save.reset(); form.reset(emptyForm); void record.refetch(); },
    status: save.data?.status ?? null,
    failure: failure === null || failure === 400 ? null
      : failure === 404 ? 'notFound' as const : failure === 403 ? 'forbidden' as const : failure === 409 ? 'changed' as const : 'error' as const,
  };
}
