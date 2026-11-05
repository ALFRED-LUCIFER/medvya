import { useEffect, useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { adjustmentError, adjustmentSchema, allChosen, emptyAdjustment, toRequest, type AdjustmentForm, type RebookAction, type SaveResult } from '../domain/adjustments';
import type { Appointment } from '../domain/schedule';
import { ApiError, fetchAdjustments, fetchScheduleDay, saveAdjustment } from '../services/scheduleApi';

/**
 * Leave and schedule exceptions (MDV-P1-012). The first save of a change that overlaps booked visits is answered with the
 * list of those visits and nothing is saved; the desk picks an action for each and saves again. Editing the form drops the list,
 * because it was worked out for the earlier dates.
 */
export function useAdjustmentController(facilityId: string) {
  const queryClient = useQueryClient();
  // The list belongs to the form values it was worked out for: editing them drops it.
  const [listed, setListed] = useState<{ forValues: string; visits: Appointment[] } | null>(null);
  const [chosen, setChosen] = useState<Record<string, RebookAction>>({});

  const day = useQuery({ queryKey: ['schedule', facilityId, 'calendars'], queryFn: () => fetchScheduleDay(facilityId, null) });
  const saved = useQuery({ queryKey: ['adjustments', facilityId], queryFn: () => fetchAdjustments(facilityId) });
  const today = day.data?.date ?? '';
  const form = useForm<AdjustmentForm>({ resolver: zodResolver(adjustmentSchema), defaultValues: emptyAdjustment('') });
  useEffect(() => {
    if (today) form.reset({ ...emptyAdjustment(today), calendarId: form.getValues('calendarId') });
  }, [today, form]);
  const edited = JSON.stringify(useWatch({ control: form.control }));
  const affected = listed?.forValues === edited ? listed.visits : null;

  const save = useMutation({
    mutationFn: (values: AdjustmentForm) => saveAdjustment(toRequest(facilityId, values, Object.fromEntries(Object.entries(chosen).filter(([id]) => affected?.some(a => a.id === id))))),
    onSuccess: () => {
      setListed(null);
      setChosen({});
      void queryClient.invalidateQueries({ queryKey: ['schedule', facilityId] });
      void queryClient.invalidateQueries({ queryKey: ['adjustments', facilityId] });
      void queryClient.invalidateQueries({ queryKey: ['front-desk'] });
    },
  });
  const error = save.error && !(save.error instanceof ApiError && save.error.code === 'actions_required')
    ? (save.error instanceof ApiError && save.error.status === 403 ? 'forbidden' as const : adjustmentError(save.error instanceof ApiError ? save.error.code : undefined))
    : null;

  return {
    form,
    calendars: (day.data?.columns ?? []).map(c => ({ id: c.calendarId, name: c.name, kind: c.calendarKind })),
    timeZone: day.data?.timeZone ?? null,
    loading: day.isPending,
    saved: saved.data ?? [],
    affected,
    chosen,
    onChoose: (appointmentId: string, action: RebookAction) => setChosen(c => ({ ...c, [appointmentId]: action })),
    canSave: !save.isPending && (affected === null || allChosen(affected, chosen)),
    saving: save.isPending,
    onSave: form.handleSubmit(values => save.mutate(values, {
      onError: e => {
        if (e instanceof ApiError && e.code === 'actions_required') setListed({ forValues: edited, visits: (e.body as { affected: Appointment[] }).affected });
      },
    })),
    result: (save.data as SaveResult | undefined) ?? null,
    error,
  };
}
