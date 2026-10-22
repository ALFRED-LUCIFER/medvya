import { useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { z } from 'zod';
import { bookingSchema, emptyBooking, slotError, type BookedAppointment, type BookingForm, type SlotChoice } from '../domain/booking';
import type { VisitType } from '../domain/schedule';
import { ApiError, bookAppointment, findPatient, holdSlot, releaseHold } from '../services/scheduleApi';
import { asSearchError, toQuery } from '../../patients/domain/directory';
import { loadState, noRetryOnAnswers, patientDataGcTime } from '../../patients/hooks/usePatientDirectoryController';
import { ApiError as PatientsApiError, searchPatients } from '../../patients/services/patientsApi';

const errorOf = (e: unknown) => slotError(e instanceof ApiError ? e.code : undefined);

/**
 * Booking from an empty slot (MDV-P1-021): clicking holds the slot (and the slots a longer visit type needs) for this user,
 * the panel finds the patient with front-desk search (MDV-P1-018: initials, age, sex and MRN tail until one is chosen), then
 * collects reason and channel, and confirming books from the hold. Closing releases the hold.
 */
export function useBookingController(facilityId: string) {
  const queryClient = useQueryClient();
  const [choice, setChoice] = useState<SlotChoice | null>(null);
  const [booked, setBooked] = useState<BookedAppointment | null>(null);
  const form = useForm<BookingForm>({ resolver: zodResolver(bookingSchema), defaultValues: emptyBooking });
  const refreshSchedule = () => queryClient.invalidateQueries({ queryKey: ['schedule', facilityId] });

  const hold = useMutation({ mutationFn: (s: SlotChoice) => holdSlot(facilityId, s), onSettled: refreshSchedule });
  const release = useMutation({ mutationFn: releaseHold, onSettled: refreshSchedule });
  const book = useMutation({
    mutationFn: (values: BookingForm) => bookAppointment(hold.data!.id, values),
    onSuccess: appointment => {
      setBooked(appointment);
      setChoice(null);
      hold.reset();
      form.reset(emptyBooking);
      clearSearch();
      void refreshSchedule();
    },
  });

  const [patientId, reasonCategory, channel] = useWatch({ control: form.control, name: ['patientId', 'reasonCategory', 'channel'] });
  const validId = z.uuid().safeParse(patientId).success;
  const patient = useQuery({ queryKey: ['booking-patient', facilityId, patientId], queryFn: () => findPatient(facilityId, patientId), enabled: validId });

  // Search runs on submit, not per keystroke (each search is audited), with the directory's caching and retry rules.
  const [searchText, setSearchText] = useState('');
  const [query, setQuery] = useState<string | null>(null);
  const search = useQuery({
    queryKey: ['patients', 'search', { query, filter: 'All', page: 1 }],
    queryFn: () => searchPatients({ query, filter: 'All', page: 1 }),
    enabled: query !== null,
    gcTime: patientDataGcTime,
    retry: noRetryOnAnswers,
  });
  const clearSearch = () => { setSearchText(''); setQuery(null); };

  const choose = (next: SlotChoice) => {
    setBooked(null);
    book.reset();
    setChoice(next);
    hold.mutate(next); // the server replaces this user's previous hold
  };

  return {
    choice,
    holding: hold.isPending,
    hold: hold.data ?? null,
    holdError: hold.isError ? errorOf(hold.error) : null,
    form,
    reasonCategory,
    channel,
    onReasonCategory: (v: BookingForm['reasonCategory']) => form.setValue('reasonCategory', v),
    onChannel: (v: BookingForm['channel']) => form.setValue('channel', v),
    patient: validId ? { loading: patient.isPending, match: patient.data ?? null } : null,
    chosenPatientId: patientId,
    patientSearch: {
      text: searchText,
      onText: setSearchText,
      onSubmit: () => setQuery(toQuery(searchText)),
      state: query === null ? ('idle' as const) : loadState(search),
      result: search.data ?? null,
      errorCode: search.error instanceof PatientsApiError ? asSearchError(search.error.code) : null,
      onPick: (id: string) => form.setValue('patientId', id, { shouldValidate: true }),
    },
    booking: book.isPending,
    bookError: book.isError ? errorOf(book.error) : null,
    booked,
    onSlot: (calendarId: string, calendarName: string, start: string) => choose({ calendarId, calendarName, start, visitType: 'FollowUp' }),
    onVisitType: (visitType: VisitType) => choice && choose({ ...choice, visitType }),
    onConfirm: form.handleSubmit(values => book.mutate(values)),
    onClose: () => {
      if (hold.data) release.mutate(hold.data.id);
      setChoice(null);
      hold.reset();
      book.reset();
      form.reset(emptyBooking);
      clearSearch();
    },
    onDismissBooked: () => setBooked(null),
  };
}

export type BookingViewModel = ReturnType<typeof useBookingController>;
