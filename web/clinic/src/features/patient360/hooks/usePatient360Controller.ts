import { useState } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import type { AccessReason } from '../../clinical-access/domain/clinicalAccess';
import { requestAccess } from '../../clinical-access/services/clinicalAccessApi';
import { ApiError } from '../../schedule/services/scheduleApi';
import { refusalOf } from '../domain/chart';
import { fetchPatient360 } from '../services/patient360Api';

/** One patient's chart (MDV-P2-004). Every open is audited by the server, so it is not retried or refetched on focus. */
export function usePatient360Controller(patientId: string) {
  const query = useQuery({ queryKey: ['patient360', patientId], queryFn: () => fetchPatient360(patientId), retry: false, refetchOnWindowFocus: false });
  const error = query.error instanceof ApiError ? query.error : null;
  // "Request access" (MDV-P2-024) from a refused chart: an owner decides; the page then says it is pending.
  const [reason, setReason] = useState<AccessReason>('SeeingPatientToday');
  const ask = useMutation({ mutationFn: () => requestAccess(patientId, reason) });
  const refusal = error?.status === 403 ? (refusalOf(error.body) ?? { canRequest: false, requestPending: false }) : null;
  return {
    reason, onReason: setReason, asking: ask.isPending, askFailed: ask.isError, onRequestAccess: () => ask.mutate(),
    data: query.data ?? null,
    loading: query.isPending,
    notFound: error?.status === 404,
    refusal: refusal && ask.isSuccess ? { ...refusal, requestPending: true } : refusal,
    failed: query.isError,
  };
}
