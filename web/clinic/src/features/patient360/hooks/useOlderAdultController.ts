import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { ApiError } from '../../schedule/services/scheduleApi';
import { emptyGeriatricForm, geriatricFormSchema, toRecord, type GeriatricForm } from '../domain/olderAdult';
import { fetchGeriatric, fetchMedicineReview, recordGeriatric } from '../services/olderAdultApi';

/**
 * The older adult cards on Patient 360 (MDV-P2-037): the latest review, a new one (always a new row) and the medicine review. Every read is
 * audited, so nothing refetches on focus. The medicine review is the doctor's (a nurse gets 403 and the card is left out); "plan change"
 * never changes a medicine: it opens the consultation, or says to start one.
 */
export function useOlderAdultController(patientId: string, enabled: boolean) {
  const queryClient = useQueryClient();
  const key = ['geriatric', patientId];
  const opts = { retry: false, refetchOnWindowFocus: false, enabled } as const;
  const history = useQuery({ queryKey: key, queryFn: () => fetchGeriatric(patientId), ...opts });
  const review = useQuery({ queryKey: ['medicine-review', patientId], queryFn: () => fetchMedicineReview(patientId), ...opts });
  const [recording, setRecording] = useState(false);
  const [planHint, setPlanHint] = useState<string | null>(null);
  const form = useForm<GeriatricForm>({ resolver: zodResolver(geriatricFormSchema), defaultValues: emptyGeriatricForm });
  const record = useMutation({
    mutationFn: (values: GeriatricForm) => recordGeriatric(patientId, toRecord(values)),
    onSuccess: () => { form.reset(emptyGeriatricForm); setRecording(false); void queryClient.invalidateQueries({ queryKey: key }); },
  });
  const status = (e: unknown) => (e instanceof ApiError ? e.status : null);
  return {
    latest: history.data?.assessments[0] ?? null,
    timeZone: history.data?.timeZone ?? 'UTC',
    rulesSignedOff: history.data?.rulesSignedOff ?? false,
    loading: history.isPending,
    failed: history.isError,
    review: review.data ?? null,
    reviewHidden: status(review.error) === 403 || status(review.error) === 404,
    reviewFailed: review.isError && status(review.error) !== 403 && status(review.error) !== 404,
    form, recording, saving: record.isPending, saveFailed: record.isError && status(record.error) !== 403,
    saveForbidden: status(record.error) === 403,
    onRecord: () => setRecording(true),
    onCancel: () => { setRecording(false); form.reset(emptyGeriatricForm); record.reset(); },
    onSubmit: form.handleSubmit(values => record.mutate(values)),
    planHint,
    onPlanChange: (medicine: string) => setPlanHint(medicine),
  };
}
