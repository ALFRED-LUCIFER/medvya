import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { ApiError } from '../../schedule/services/scheduleApi';
import { closureSchema, settingsError, type ClosureForm } from '../domain/settings';
import { addClosure, fetchClosures, removeClosure } from '../services/settingsApi';

/** Holidays tab: closed dates of a location. Each lists the visits already booked that day, which the desk has to move. */
export function useHolidaysController(locationId: string) {
  const queryClient = useQueryClient();
  const key = ['settings', 'closures', locationId];
  const closures = useQuery({ queryKey: key, queryFn: () => fetchClosures(locationId), enabled: !!locationId });
  const form = useForm<ClosureForm>({ resolver: zodResolver(closureSchema), defaultValues: { date: '', name: '' }, shouldFocusError: false });
  const refresh = () => void queryClient.invalidateQueries({ queryKey: key });
  const add = useMutation({
    mutationFn: (values: ClosureForm) => addClosure(locationId, values),
    onSuccess: () => { form.reset({ date: '', name: '' }); refresh(); },
  });
  const remove = useMutation({ mutationFn: removeClosure, onSuccess: refresh });

  const addError: 'duplicate' | 'failed' | null = !add.isError ? null
    : settingsError(add.error instanceof ApiError ? add.error.code : undefined) === 'date:duplicate' ? 'duplicate' : 'failed';

  return {
    form,
    closures: closures.data ?? [],
    loading: closures.isPending && !!locationId,
    loadFailed: closures.isError,
    forbidden: closures.error instanceof ApiError && closures.error.status === 403,
    adding: add.isPending,
    addError,
    removingId: remove.isPending ? remove.variables : null,
    removeFailed: remove.isError,
    onSubmit: form.handleSubmit(values => add.mutate(values)),
    onRemove: (id: string) => remove.mutate(id),
  };
}
