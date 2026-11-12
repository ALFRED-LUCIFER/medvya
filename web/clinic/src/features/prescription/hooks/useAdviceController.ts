import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { useForm } from 'react-hook-form';
import { ApiError } from '../../schedule/services/scheduleApi';
import { adviceSchema, type AdviceForm, type RxAdvice } from '../domain/authorisation';
import { fetchAdvice, saveAdvice } from '../services/prescriptionApi';

/**
 * The advice for the patient in the plan (MDV-P2-043): written once, saved on request, frozen with the prescription when it is authorised.
 * A refusal (403) hides it, as for the composer.
 */
export function useAdviceController(encounterId: string) {
  const queryClient = useQueryClient();
  const key = ['rx-advice', encounterId];
  const query = useQuery({ queryKey: key, queryFn: () => fetchAdvice(encounterId), retry: false });
  const form = useForm<AdviceForm>({ resolver: zodResolver(adviceSchema), defaultValues: { text: '' } });
  const { reset } = form;
  useEffect(() => { if (query.data) reset({ text: query.data.text ?? '' }); }, [query.data, reset]);
  const save = useMutation({
    mutationFn: (v: AdviceForm) => saveAdvice(encounterId, v.text),
    // A changed advice means the authorised paper no longer matches: the composer reads its state again.
    onSuccess: (a: RxAdvice) => { queryClient.setQueryData(key, a); void queryClient.invalidateQueries({ queryKey: ['prescription-authorisation', encounterId] }); },
  });
  const error = query.error instanceof ApiError ? query.error : null;
  return {
    advice: query.data ?? null,
    loading: query.isPending,
    hidden: error?.status === 403,
    failed: query.isError && error?.status !== 403,
    form,
    saving: save.isPending,
    saved: save.isSuccess && !form.formState.isDirty,
    saveFailed: save.isError,
    onSave: form.handleSubmit(v => save.mutate(v)),
  };
}
