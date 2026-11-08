import { useEffect } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { ApiError } from '../../schedule/services/scheduleApi';
import { clinicalPolicySchema, type ClinicalPolicyForm } from '../domain/clinicalAccess';
import { fetchClinicalPolicy, saveClinicalPolicy } from '../services/clinicalAccessApi';

const key = ['settings', 'clinical-access'];

/** Who treats the facility's patients (MDV-P2-024). The server allows only an owner to change it and audits each change. */
export function useClinicalPolicyController() {
  const queryClient = useQueryClient();
  const query = useQuery({ queryKey: key, queryFn: fetchClinicalPolicy });
  const form = useForm<ClinicalPolicyForm>({ resolver: zodResolver(clinicalPolicySchema), defaultValues: { policy: 'AllFacilityClinicians' } });
  const { reset } = form;
  useEffect(() => {
    if (query.data) reset(query.data);
  }, [query.data, reset]);
  const save = useMutation({ mutationFn: (values: ClinicalPolicyForm) => saveClinicalPolicy(values.policy), onSuccess: data => queryClient.setQueryData(key, data) });
  return {
    form,
    loading: query.isPending,
    forbidden: query.error instanceof ApiError && query.error.status === 403,
    loadFailed: query.isError,
    saving: save.isPending,
    saved: save.isSuccess && !form.formState.isDirty,
    saveFailed: save.isError,
    onSubmit: form.handleSubmit(values => save.mutate(values)),
  };
}
