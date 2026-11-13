import { useEffect, useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { emptyWalkIn, inToday, newPatientWalkIn, walkInError, type WalkInChoice, type WalkInVisit } from '../domain/board';
import { checkIn, heldWalkInStart, holdWalkInStart, monotonicNow, walkIn } from '../services/frontDeskApi';
import { ApiError, fetchScheduleDay, findPatient } from '../../schedule/services/scheduleApi';
import { usePatientSearch } from '../../patients/hooks/usePatientSearch';

/**
 * Walk-in (MDV-P1-025): find the patient (or arrive here from quick registration with the new patient chosen), choose a doctor
 * or the next available one, then one tap books the next free slot today on the booking path (or queues the visit with no free
 * slot) and checks it in with the board's check-in. The time from opening Walk-in to checked in is the check-in duration (exit
 * gate 3, MDV-P1-106): it runs on through quick registration, and a visit that arrives with the patient already chosen from
 * anywhere else (global search, the patient record) has no start we saw, so it is sent as not measured rather than as an instant.
 * shortcut: the desk's search before such an arrival is not timed; start the clock in global search when those check-ins matter.
 */
export function useWalkInController(facilityId: string, initialPatientId: string | null) {
  const queryClient = useQueryClient();
  const opened = useRef(initialPatientId ? heldWalkInStart() : monotonicNow());
  // A held start is for this arrival only: a later one must not inherit it.
  useEffect(() => holdWalkInStart(null), []);
  const [patientId, setPatientId] = useState(initialPatientId ?? '');
  // Arriving from quick registration means a new patient: a new consultation by default.
  const [choice, setChoice] = useState<WalkInChoice>(initialPatientId ? newPatientWalkIn : emptyWalkIn);

  // Name and MRN of the chosen patient (an audited lookup), as booking confirms them.
  const patient = useQuery({ queryKey: ['booking-patient', facilityId, patientId], queryFn: () => findPatient(facilityId, patientId), enabled: patientId !== '', staleTime: Infinity });
  // The doctors to choose from: the day view's doctor columns, without those who are not in today (no hours, or on leave all day).
  const day = useQuery({ queryKey: ['schedule', facilityId, 'walk-in'], queryFn: () => fetchScheduleDay(facilityId, null) });
  const doctors = (day.data?.columns ?? []).filter(c => c.calendarKind === 'Doctor' && inToday(c)).map(c => ({ id: c.calendarId, name: c.name }));

  const done = useMutation({
    mutationFn: async (allowRepeat: boolean): Promise<WalkInVisit & { token: string | null }> => {
      const visit = await walkIn(facilityId, patientId, choice, allowRepeat);
      const card = await checkIn(visit.id, opened.current === null ? null : monotonicNow() - opened.current); // the board's own check-in: the same state machine
      return { ...visit, token: card.token };
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['front-desk'] }),
  });
  // Another patient clears the last answer: a "visit today" warning must never carry over to them.
  const search = usePatientSearch(id => { setPatientId(id); done.reset(); });
  const error = done.error ? (done.error instanceof ApiError && done.error.status === 403 && done.error.code !== 'terms_not_accepted' ? 'forbidden' as const : walkInError(done.error instanceof ApiError ? done.error.code : undefined)) : null;

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
    onConfirm: () => done.mutate(false),
    /** Leaving for quick registration: the clock opened here keeps running until the new patient is checked in. */
    onRegister: () => holdWalkInStart(opened.current),
    /** After the "already has a visit today" warning: make the second visit anyway. */
    onConfirmRepeat: () => done.mutate(true),
    result: done.data ?? null,
    error,
  };
}
