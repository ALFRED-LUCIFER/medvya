import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { changeBody, type ConsentRow, type PatientConsents } from '../domain/consents';
import { changeConsent, fetchConsents } from '../services/patientsApi';
import { patientDataGcTime } from './usePatientDirectoryController';

/** The consent section of one record: current decisions, and grant / withdraw as a new receipt. The server checks facility and role. */
export function usePatientConsentsController(patientId: string): PatientConsents {
  const queryClient = useQueryClient();
  const key = ['patients', 'consents', patientId];
  // Not Infinity like the record: another desk may change a consent, so it is read again after a minute (each read is audited).
  const states = useQuery({ queryKey: key, queryFn: () => fetchConsents(patientId), gcTime: patientDataGcTime, staleTime: 60_000 });
  const change = useMutation({
    mutationFn: ({ row, granted }: { row: ConsentRow; granted: boolean }) => changeConsent(patientId, changeBody(row, granted)),
    onSettled: () => queryClient.invalidateQueries({ queryKey: key }), // a refused change (e.g. a newer decision elsewhere) shows the truth too
  });
  return {
    states: states.data ?? [], loading: states.isPending, failed: states.isError,
    changing: change.isPending ? change.variables.row.id : null, changeFailed: change.isError,
    onChange: (row: ConsentRow, granted: boolean) => change.mutate({ row, granted }),
  };
}
