import { useQuery } from '@tanstack/react-query';
import { errorKey } from '../../api/domain/errors';
import { useSignedIn } from '../../session/hooks/session';
import { fetchPatientRecord } from '../services/patients';

/** The patient summary. Front desk sees demographics only; the clinical part is for clinicians (the API enforces it too). */
export function usePatientRecordController(id: string) {
  const { session } = useSignedIn();
  const query = useQuery({ queryKey: ['patient', id], queryFn: () => fetchPatientRecord(id) });
  return {
    session,
    record: query.data ?? null,
    clinical: session.staff.role === 'doctor',
    loading: query.isPending,
    notFound: query.isSuccess && !query.data,
    error: query.error ? errorKey(query.error) : null,
  };
}
