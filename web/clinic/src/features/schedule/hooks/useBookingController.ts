import { useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { z } from 'zod';
import { bookingSchema, emptyBooking, slotError, type BookedAppointment, type BookingForm, type SlotChoice } from '../domain/booking';
import type { VisitType } from '../domain/schedule';
import { ApiError, bookAppointment, findPatient, holdSlot, releaseHold } from '../services/scheduleApi';

const errorOf = (e: unknown) => slotError(e instanceof ApiError ? e.code : undefined);

/**
 * Booking from an empty slot (MDV-P1-021): clicking holds the slot (and the slots a longer visit type needs) for this user,
 * the panel collects patient, reason and channel, and confirming books from the hold. Closing releases the hold.
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
      void refreshSchedule();
    },
  });

  const [patientId, reasonCategory, channel] = useWatch({ control: form.control, name: ['patientId', 'reasonCategory', 'channel'] });
  const validId = z.uuid().safeParse(patientId).success;
  // An audited read (patient.lookup): fetched once per id, never again on focus or remount while cached.
  const patient = useQuery({ queryKey: ['booking-patient', facilityId, patientId], queryFn: () => findPatient(facilityId, patientId), enabled: validId, staleTime: Infinity });

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
    },
    onDismissBooked: () => setBooked(null),
  };
}

export type BookingViewModel = ReturnType<typeof useBookingController>;
