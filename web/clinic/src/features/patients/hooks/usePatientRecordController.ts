import { useQuery } from '@tanstack/react-query';
import { fetchPatient } from '../services/patientsApi';
import { loadState, noRetryOnAnswers, patientDataGcTime } from './usePatientDirectoryController';

/** One opened record. The server audits the read (purpose "registration") before it answers; the data stays in memory only. */
export function usePatientRecordController(patientId: string) {
  const record = useQuery({
    queryKey: ['patients', 'record', patientId],
    queryFn: () => fetchPatient(patientId), // no abort signal (see usePatientDirectoryController)
    gcTime: patientDataGcTime,
    staleTime: Infinity, // each fetch is an audited read: reuse it until something changes it
    retry: noRetryOnAnswers,
  });
  return { patient: record.data ?? null, state: loadState(record), onRetry: () => void record.refetch() };
}
