import { useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { emptyWalkIn, walkInError, type WalkInChoice, type WalkInVisit } from '../domain/board';
import { checkIn, monotonicNow, walkIn } from '../services/frontDeskApi';
import { ApiError, fetchScheduleDay, findPatient } from '../../schedule/services/scheduleApi';
import { usePatientSearch } from '../../patients/hooks/usePatientSearch';

/**
 * Walk-in (MDV-P1-025): find the patient (or arrive here from quick registration with the new patient chosen), choose a doctor
 * or the next available one, then one tap books the next free slot today on the booking path (or queues the visit with no free
 * slot) and checks it in with the board's check-in. The time from opening to checked in is the check-in duration (exit gate 3).
 */
export function useWalkInController(facilityId: string, initialPatientId: string | null) {
  const queryClient = useQueryClient();
  const opened = useRef(monotonicNow());
  const [patientId, setPatientId] = useState(initialPatientId ?? '');
  const [choice, setChoice] = useState<WalkInChoice>(emptyWalkIn);
  const search = usePatientSearch(setPatientId);

  // Name and MRN of the chosen patient (an audited lookup), as booking confirms them.
  const patient = useQuery({ queryKey: ['booking-patient', facilityId, patientId], queryFn: () => findPatient(facilityId, patientId), enabled: patientId !== '', staleTime: Infinity });
  // The doctors to choose from: the day view's doctor columns.
  const day = useQuery({ queryKey: ['schedule', facilityId, 'walk-in'], queryFn: () => fetchScheduleDay(facilityId, null) });
  const doctors = (day.data?.columns ?? []).filter(c => c.calendarKind === 'Doctor').map(c => ({ id: c.calendarId, name: c.name }));

  const done = useMutation({
    mutationFn: async (): Promise<WalkInVisit> => {
      const visit = await walkIn(facilityId, patientId, choice);
      await checkIn(visit.id, monotonicNow() - opened.current); // the board's own check-in: the same state machine
      return visit;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['front-desk'] }),
  });
  const error = done.error ? (done.error instanceof ApiError && done.error.status === 403 ? 'forbidden' as const : walkInError(done.error instanceof ApiError ? done.error.code : undefined)) : null;

  return {
    patientSearch: search.view,
    patientId,
    patient: patientId ? { loading: patient.isPending, match: patient.data ?? null } : null,
    doctors,
    doctorsLoading: day.isPending,
    timeZone: day.data?.timeZone ?? null,
    choice,
    onChoice: (next: Partial<WalkInChoice>) => { setChoice(c => ({ ...c, ...next })); done.reset(); },
    canConfirm: patientId !== '' && !!patient.data && !done.isPending,
    saving: done.isPending,
    onConfirm: () => done.mutate(),
    result: done.data ?? null,
    error,
  };
}
