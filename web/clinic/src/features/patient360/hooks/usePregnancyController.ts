import { useState } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { ApiError } from '../../schedule/services/scheduleApi';
import type { Patient360, PregnancyStatus } from '../domain/chart';
import {
  closeSchema, pregnancyConflicts, readingsBody, readingsSchema, startBody, startSchema, type CloseForm, type PregnancyForm, type PregnancyProblem, type ReadingsForm, type StartForm,
} from '../domain/pregnancy';
import { closePregnancy, counselDangerSigns, recordPregnancyReadings, ruleOutPregnancy, startPregnancy } from '../services/pregnancyApi';

/**
 * The pregnancy card (MDV-P2-034). Reads the Patient 360 answer it is given. A write changes the life stage, so the chart is read again
 * (and audited) after it. `canManage` (a doctor) and `canRecord` (a doctor or nurse) only hide buttons: the server decides every write.
 */
export function usePregnancyController(patientId: string, data: Patient360 | null, canManage: boolean, canRecord: boolean) {
  const queryClient = useQueryClient();
  const [form, setForm] = useState<PregnancyForm | null>(null);
  const startForm = useForm<StartForm>({ resolver: zodResolver(startSchema), defaultValues: {} });
  const closeForm = useForm<CloseForm>({ resolver: zodResolver(closeSchema) });
  const readingsForm = useForm<ReadingsForm>({ resolver: zodResolver(readingsSchema), defaultValues: {} });
  const write = useMutation({
    mutationFn: (run: () => Promise<PregnancyStatus>) => run(),
    onSuccess: async () => {
      setForm(null);
      await queryClient.invalidateQueries({ queryKey: ['patient360', patientId] });
    },
  });
  const error = write.error instanceof ApiError ? write.error : null;
  const problem: PregnancyProblem | null = !write.isError ? null
    : error?.status === 403 ? 'refused'
    : error?.status === 409 && pregnancyConflicts.includes(error.code as (typeof pregnancyConflicts)[number]) ? error.code as PregnancyProblem
    : error?.status === 400 ? 'invalid' : 'failed';
  const open = (f: PregnancyForm) => {
    write.reset();
    if (f === 'start') startForm.reset({});
    if (f === 'readings') readingsForm.reset({});
    setForm(f);
  };
  return {
    // Offered to a patient who is not male and is 10 or older; an open one always shows (one recorded by mistake can then be closed).
    shown: !!data && (!!data.pregnancy.open || (data.patient.sexAtBirth !== 'Male' && data.patient.ageYears >= 10)),
    status: data?.pregnancy ?? { open: null, ruledOut: null },
    timeZone: data?.timeZone ?? 'UTC',
    canManage, canRecord, form, startForm, closeForm, readingsForm,
    saving: write.isPending, problem,
    onOpen: open,
    onCancel: () => setForm(null),
    onStart: startForm.handleSubmit(v => write.mutate(() => startPregnancy(patientId, startBody(v)))),
    onClose: closeForm.handleSubmit(v => write.mutate(() => closePregnancy(patientId, v.outcome))),
    onReadings: readingsForm.handleSubmit(v => write.mutate(() => recordPregnancyReadings(patientId, readingsBody(v)))),
    onCounselled: () => write.mutate(() => counselDangerSigns(patientId)),
    onRuledOut: () => write.mutate(() => ruleOutPregnancy(patientId)),
  };
}
