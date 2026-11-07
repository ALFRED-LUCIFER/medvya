import { useEffect, useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { adjustmentError, adjustmentSchema, allChosen, emptyAdjustment, manageableCalendars, toForm, toRequest, type AdjustmentForm, type RebookAction, type SaveResult, type SavedAdjustment } from '../domain/adjustments';
import type { Appointment } from '../domain/schedule';
import { ApiError, fetchAdjustments, deleteAdjustment, fetchScheduleDay, saveAdjustment, updateAdjustment } from '../services/scheduleApi';

/**
 * Leave and schedule exceptions (MDV-P1-012). The first save of a change that overlaps booked visits is answered with the
 * list of those visits and nothing is saved; the desk picks an action for each and saves again. Editing the form drops the list,
 * because it was worked out for the earlier dates.
 */
export function useAdjustmentController(facilityId: string, canConfigure: boolean, userId: string) {
  const queryClient = useQueryClient();
  // The list belongs to the form values it was worked out for: editing them drops it.
  const [listed, setListed] = useState<{ forValues: string; visits: Appointment[] } | null>(null);
  const [editing, setEditing] = useState<{ id: string; version: string } | null>(null);
  const editingId = editing?.id ?? null;
  const [confirmingId, setConfirmingId] = useState<string | null>(null);
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

  // Someone else changed the row meanwhile: show the current list, and edit or delete again from it.
  const onStale = (e: Error) => {
    if (!(e instanceof ApiError && adjustmentError(e.code) === 'stale')) return;
    setEditing(null);
    void queryClient.invalidateQueries({ queryKey: ['adjustments', facilityId] });
  };
  const save = useMutation({
    onError: onStale,
    mutationFn: (values: AdjustmentForm) => {
      const body = toRequest(facilityId, values, Object.fromEntries(Object.entries(chosen).filter(([id]) => affected?.some(a => a.id === id))));
      return editing ? updateAdjustment(editing.id, { ...body, version: editing.version }) : saveAdjustment(body);
    },
    onSuccess: () => {
      setListed(null);
      setEditing(null);
      setChosen({});
      void queryClient.invalidateQueries({ queryKey: ['schedule', facilityId] });
      void queryClient.invalidateQueries({ queryKey: ['adjustments', facilityId] });
      void queryClient.invalidateQueries({ queryKey: ['front-desk'] });
    },
  });
  const remove = useMutation({
    onError: onStale,
    mutationFn: (row: { id: string; version: string }) => deleteAdjustment(facilityId, row.id, row.version),
    onSuccess: (_, row) => {
      setConfirmingId(null);
      if (editingId === row.id) setEditing(null);
      void queryClient.invalidateQueries({ queryKey: ['schedule', facilityId] });
      void queryClient.invalidateQueries({ queryKey: ['adjustments', facilityId] });
    },
  });
  const error = save.error && !(save.error instanceof ApiError && save.error.code === 'actions_required')
    ? (save.error instanceof ApiError && save.error.status === 403 ? 'forbidden' as const : adjustmentError(save.error instanceof ApiError ? save.error.code : undefined))
    : null;

  return {
    form,
    calendars: (day.data?.columns ?? []).map(c => ({ id: c.calendarId, name: c.name, kind: c.calendarKind })),
    /** The calendars this person may add leave to; empty for the front desk, who only see and rebook. */
    manageable: manageableCalendars((day.data?.columns ?? []).map(c => ({ id: c.calendarId, name: c.name })), canConfigure, userId),
    timeZone: day.data?.timeZone ?? null,
    loading: day.isPending,
    saved: saved.data ?? [],
    editingId,
    onEdit: (row: SavedAdjustment) => { save.reset(); setListed(null); setChosen({}); setEditing({ id: row.id, version: row.version }); form.reset(toForm(row)); },
    onCancelEdit: () => { setEditing(null); form.reset({ ...emptyAdjustment(today), calendarId: form.getValues('calendarId') }); },
    confirmingId,
    onAskDelete: (id: string | null) => { remove.reset(); setConfirmingId(id); },
    onDelete: (row: SavedAdjustment) => remove.mutate(row),
    deleting: remove.isPending,
    deleteError: remove.error ? adjustmentError(remove.error instanceof ApiError ? remove.error.code : undefined) : null,
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
