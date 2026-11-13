import { useEffect } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { ApiError } from '../../schedule/services/scheduleApi';
import { queueRuleSchema, type QueueRule } from '../domain/settings';
import { fetchQueueRule, saveQueueRule } from '../services/settingsApi';

const key = ['settings', 'queue-rule'];

/** The clinic's queue rule (MDV-P1-103). The server allows only an owner to change it and audits each change. */
export function useQueueRuleController() {
  const queryClient = useQueryClient();
  const query = useQuery({ queryKey: key, queryFn: fetchQueueRule });
  const form = useForm<QueueRule>({ resolver: zodResolver(queueRuleSchema), defaultValues: { order: 'Arrival', bookedFirst: false } });
  const { reset } = form;
  useEffect(() => {
    if (query.data) reset(query.data);
  }, [query.data, reset]);
  const save = useMutation({ mutationFn: saveQueueRule, onSuccess: data => queryClient.setQueryData(key, data) });
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
