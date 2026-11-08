import { useQuery } from '@tanstack/react-query';
import { ApiError } from '../../schedule/services/scheduleApi';
import { refusalOf } from '../domain/chart';
import { fetchPatient360 } from '../services/patient360Api';

/** One patient's chart (MDV-P2-004). Every open is audited by the server, so it is not retried or refetched on focus. */
export function usePatient360Controller(patientId: string) {
  const query = useQuery({ queryKey: ['patient360', patientId], queryFn: () => fetchPatient360(patientId), retry: false, refetchOnWindowFocus: false });
  const error = query.error instanceof ApiError ? query.error : null;
  return {
    data: query.data ?? null,
    loading: query.isPending,
    notFound: error?.status === 404,
    refusal: error?.status === 403 ? (refusalOf(error.body) ?? { canRequest: false, requestPending: false }) : null,
    failed: query.isError,
  };
}
