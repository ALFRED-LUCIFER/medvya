import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { usePatientSearch } from '../../patients/hooks/usePatientSearch';
import { ApiError, fetchScheduleDay, findPatient } from '../../schedule/services/scheduleApi';
import { emptyWaitlistForm, waitlistError, waitlistSchema, type Outcome, type WaitlistEntry, type WaitlistForm } from '../domain/waitlist';
import { acceptOffer, addToWaitlist, declineOffer, fetchWaitlist, offerSlot, removeFromWaitlist } from '../services/waitlistApi';

/**
 * The waitlist (MDV-P1-027): add a patient with preferences, offer a freed slot (the server holds it and messages the patient),
 * record the patient's answer (accept books it on the booking path; decline releases it). The list is refetched after every
 * change and every 30 seconds, since matches change as the day's bookings do.
 */
export function useWaitlistController(facilityId: string) {
  const queryClient = useQueryClient();
  const [patientId, setPatientId] = useState('');
  const [outcome, setOutcome] = useState<Outcome | null>(null);
  const list = useQuery({ queryKey: ['waitlist', facilityId], queryFn: () => fetchWaitlist(facilityId), refetchInterval: 30_000 });
  const day = useQuery({ queryKey: ['schedule', facilityId, 'waitlist'], queryFn: () => fetchScheduleDay(facilityId, null) });
  const patient = useQuery({ queryKey: ['booking-patient', facilityId, patientId], queryFn: () => findPatient(facilityId, patientId), enabled: patientId !== '', staleTime: Infinity });
  const form = useForm<WaitlistForm>({ resolver: zodResolver(waitlistSchema), defaultValues: emptyWaitlistForm });

  const refresh = () => { void queryClient.invalidateQueries({ queryKey: ['waitlist', facilityId] }); void queryClient.invalidateQueries({ queryKey: ['front-desk'] }); };
  const done = (o: Outcome) => { setOutcome(o); refresh(); };
  const add = useMutation({
    mutationFn: (v: WaitlistForm) => addToWaitlist(facilityId, patientId, v),
    onSuccess: () => { form.reset(emptyWaitlistForm); setPatientId(''); search.clear(); done({ kind: 'added' }); },
  });
  const search = usePatientSearch(id => { setPatientId(id); add.reset(); });
  const act = useMutation({
    mutationFn: async (a: { kind: 'offer' | 'accept' | 'decline' | 'remove'; entry: WaitlistEntry }): Promise<Outcome> => {
      const { kind, entry } = a;
      if (kind === 'offer') await offerSlot(entry.id, entry.match!.calendarId, entry.match!.start);
      else if (kind === 'decline') await declineOffer(entry.id);
      else if (kind === 'remove') await removeFromWaitlist(entry.id);
      else return { kind: 'booked', visit: await acceptOffer(entry.id) };
      return { kind: kind === 'offer' ? 'offered' : kind === 'decline' ? 'declined' : 'removed' };
    },
    onSuccess: done,
    onError: refresh, // another desk changed it first: show the list as it is now
  });
  const failure = add.error ?? act.error;
  const error = failure
    ? (failure instanceof ApiError && failure.status === 403 && failure.code !== 'terms_not_accepted' ? 'forbidden' as const : waitlistError(failure instanceof ApiError ? failure.code : undefined))
    : null;

  return {
    form,
    patientSearch: search.view,
    patientId,
    patient: patientId ? { loading: patient.isPending, match: patient.data ?? null } : null,
    doctors: (day.data?.columns ?? []).filter(c => c.calendarKind === 'Doctor').map(c => ({ id: c.calendarId, name: c.name })),
    entries: list.data?.entries ?? [],
    timeZone: list.data?.timeZone ?? day.data?.timeZone ?? null,
    loading: list.isPending,
    failedToLoad: list.isError && !(list.error instanceof ApiError && list.error.status === 403),
    forbidden: list.error instanceof ApiError && list.error.status === 403,
    canAdd: patientId !== '' && !!patient.data && !add.isPending,
    adding: add.isPending,
    onAdd: form.handleSubmit(v => add.mutate(v)),
    pendingId: act.isPending ? act.variables.entry.id : null,
    onOffer: (entry: WaitlistEntry) => act.mutate({ kind: 'offer', entry }),
    onAccept: (entry: WaitlistEntry) => act.mutate({ kind: 'accept', entry }),
    onDecline: (entry: WaitlistEntry) => act.mutate({ kind: 'decline', entry }),
    onRemove: (entry: WaitlistEntry) => act.mutate({ kind: 'remove', entry }),
    outcome: failure ? null : outcome,
    error,
  };
}
