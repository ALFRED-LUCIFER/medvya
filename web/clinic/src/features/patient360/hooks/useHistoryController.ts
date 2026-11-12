import { useState } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { ApiError } from '../../schedule/services/scheduleApi';
import type { HistorySection, Patient360 } from '../domain/chart';
import { bloodGroupSchema, historySchema, historySections, type BloodGroupForm, type HistoryForm } from '../domain/history';
import { saveHistory } from '../services/historyApi';

/**
 * The chart's history sections and blood group (MDV-P2-032). Reads the Patient 360 answer it is given; a save answers the whole history,
 * which replaces the cached one (no second audited chart read). `canEdit` only hides the buttons: the server decides every save.
 */
export function useHistoryController(patientId: string, data: Patient360 | null, canEdit: boolean) {
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState<HistorySection | null>(null);
  const form = useForm<HistoryForm>({ resolver: zodResolver(historySchema), defaultValues: { text: '' } });
  const bloodForm = useForm<BloodGroupForm>({ resolver: zodResolver(bloodGroupSchema) });
  const save = useMutation({
    mutationFn: ({ section, text }: { section: HistorySection; text: string }) => saveHistory(patientId, section, text),
    onSuccess: ({ items }) => {
      queryClient.setQueryData<Patient360>(['patient360', patientId], old =>
        old && { ...old, chart: { ...old.chart, history: items, bloodGroup: items.find(i => i.section === 'BloodGroup')?.text ?? null } });
      setEditing(null);
    },
  });
  const error = save.error instanceof ApiError ? save.error : null;
  const start = (section: HistorySection) => {
    save.reset();
    const current = data?.chart.history.find(h => h.section === section)?.text ?? '';
    if (section === 'BloodGroup') bloodForm.reset(bloodGroupSchema.safeParse({ text: current }).data);
    else form.reset({ text: current });
    setEditing(section);
  };
  return {
    sections: data ? historySections(data.historySections, data.chart.history) : [],
    bloodGroup: data?.chart.bloodGroup ?? null,
    timeZone: data?.timeZone ?? 'UTC',
    canEdit, editing, form, bloodForm,
    saving: save.isPending,
    problem: save.isError ? (error?.status === 403 ? 'refused' as const : 'failed' as const) : null,
    onEdit: start,
    onCancel: () => setEditing(null),
    onSave: form.handleSubmit(d => editing && save.mutate({ section: editing, text: d.text })),
    onSaveBloodGroup: bloodForm.handleSubmit(d => save.mutate({ section: 'BloodGroup', text: d.text })),
  };
}
