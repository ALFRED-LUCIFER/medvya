import { useEffect, useRef, useState } from 'react';
import { useSearchParams } from 'react-router';
import { useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { z } from 'zod';
import { bookingSchema, emptyBooking, readSlotReturn, slotError, withoutSlotReturn, type BookedAppointment, type BookingForm, type SlotChoice } from '../domain/booking';
import type { ScheduleColumn, VisitType } from '../domain/schedule';
import { includedStarts, maxRepeat, moveRow, planOf, replanOf, summarise, toggleRow, type PlanRow, type SeriesDone } from '../domain/series';
import { forgetKey, type HeldKey, keyFor } from '../../api/services/idempotencyKey';
import { ApiError, bookAppointment, bookSeries, findPatient, holdSlot, previewSeries, releaseHold } from '../services/scheduleApi';
import { usePatientSearch } from '../../patients/hooks/usePatientSearch';

const errorOf = (e: unknown) => slotError(e instanceof ApiError ? e.code : undefined);

/**
 * Booking from an empty slot (MDV-P1-021): clicking holds the slot (and the slots a longer visit type needs) for this user,
 * the panel finds the patient with front-desk search (MDV-P1-018: initials, age, sex and MRN tail until one is chosen), then
 * collects reason and channel, and confirming books from the hold. Closing releases the hold. Back from registering a new patient,
 * the address names the slot and the patient: the slot is held again with the patient chosen.
 */
export function useBookingController(facilityId: string, timeZone: string | null = null, columns: Pick<ScheduleColumn, 'calendarId' | 'name'>[] | undefined = undefined) {
  const queryClient = useQueryClient();
  const [params, setParams] = useSearchParams();
  // Back from registering a new patient: the slot to hold again and the patient to choose (read once, at mount).
  const [resumed] = useState(() => { const back = readSlotReturn(params); return back && { choice: { ...back, calendarName: '' }, patientId: back.patientId }; });
  // From the global search's "Book" (MDV-P2-003): the address names a patient but no slot, and the next slot chosen is booked for them.
  const [carried, setCarried] = useState(() => { const id = params.get('patient') ?? ''; return !readSlotReturn(params) && z.uuid().safeParse(id).success ? id : ''; });
  const [choice, setChoice] = useState<SlotChoice | null>(resumed?.choice ?? null);
  const [booked, setBooked] = useState<BookedAppointment | null>(null);
  // A no-show's rebook (MDV-P1-024): the next slot chosen is booked for this patient, already filled in.
  const [rebook, setRebook] = useState<{ id: string; name: string } | null>(null);
  // MDV-P1-026: repeat weekly or monthly. 1 books one visit; more previews the series so the desk can skip or move conflicts before confirming.
  const [repeat, setRepeat] = useState(1);
  const [unit, setUnit] = useState<'weekly' | 'monthly' | 'weekdays'>('weekly');
  const [days, setDays] = useState<number[]>([]); // for 'weekdays': 0 = Sunday, as the server numbers them
  const [plan, setPlan] = useState<PlanRow[] | null>(null);
  const [seriesDone, setSeriesDone] = useState<SeriesDone | null>(null);
  const form = useForm<BookingForm>({ resolver: zodResolver(bookingSchema), defaultValues: { ...emptyBooking, patientId: resumed?.patientId ?? '' } });
  const refreshSchedule = () => queryClient.invalidateQueries({ queryKey: ['schedule', facilityId] });

  const hold = useMutation({ mutationFn: (s: SlotChoice) => holdSlot(facilityId, s), onSettled: refreshSchedule });
  const release = useMutation({ mutationFn: releaseHold, onSettled: refreshSchedule });
  const bookKey: HeldKey = useRef(null); // one key per submission, kept across retries and taps
  const book = useMutation({
    mutationFn: (values: BookingForm) => bookAppointment(hold.data!.id, values, keyFor(bookKey, { hold: hold.data!.id, ...values })),
    onSuccess: appointment => {
      forgetKey(bookKey);
      setBooked(appointment);
      setRebook(null);
      setCarried('');
      setChoice(null);
      hold.reset();
      form.reset(emptyBooking);
      clearSearch();
      void refreshSchedule();
    },
  });

  const endBooking = () => {
    setPlan(null);
    setRepeat(1);
    setUnit('weekly');
    setDays([]);
    setChoice(null);
    hold.reset();
    form.reset(emptyBooking);
    clearSearch();
  };
  const preview = useMutation({
    mutationFn: () => previewSeries(facilityId, choice!, hold.data!.start, repeat, unit === 'monthly', unit === 'weekdays' ? days : []),
    onSuccess: occurrences => setPlan(planOf(occurrences)),
  });
  const bookMany = useMutation({
    mutationFn: (values: BookingForm) => bookSeries(facilityId, choice!, values, includedStarts(plan!)),
    onSuccess: result => {
      // All or nothing: a refused series saved nothing, so the desk fixes the conflicts in the review and confirms again.
      if (result.occurrences.some(o => !o.appointment)) { setPlan(replanOf(result)); return; }
      setSeriesDone(summarise(patient.data?.fullName ?? '', result));
      if (hold.data) release.mutate(hold.data.id); // the series did not use the hold
      endBooking();
      void refreshSchedule();
    },
  });

  const [patientId, reasonCategory, channel, reasonText] = useWatch({ control: form.control, name: ['patientId', 'reasonCategory', 'channel', 'reasonText'] });
  const validId = z.uuid().safeParse(patientId).success;
  // An audited read (patient.lookup): fetched once per id, never again on focus or remount while cached.
  const patient = useQuery({ queryKey: ['booking-patient', facilityId, patientId], queryFn: () => findPatient(facilityId, patientId), enabled: validId, staleTime: Infinity });

  const search = usePatientSearch(id => form.setValue('patientId', id, { shouldValidate: true }));
  const clearSearch = search.clear;

  const choose = (next: SlotChoice) => {
    setBooked(null);
    setSeriesDone(null);
    setPlan(null);
    book.reset();
    setChoice(next);
    if (rebook || carried) form.setValue('patientId', rebook?.id ?? carried, { shouldValidate: true });
    hold.mutate(next); // the server replaces this user's previous hold
  };

  const holdNow = hold.mutate;
  useEffect(() => {
    if (!resumed || !readSlotReturn(params)) return; // only while the address still names the slot
    // A still-valid hold is replaced, an expired one re-acquired; a taken slot answers with its error in the panel.
    holdNow(resumed.choice);
    setParams(withoutSlotReturn(params), { replace: true }); // a reload does not resume again
  }, [resumed, params, setParams, holdNow]);

  return {
    // The calendar's name comes from the day once loaded (a resumed booking knows only its id).
    choice: choice && { ...choice, calendarName: choice.calendarName || columns?.find(c => c.calendarId === choice.calendarId)?.name || '' },
    holding: hold.isPending,
    hold: hold.data ?? null,
    holdError: hold.isError ? errorOf(hold.error) : null,
    form,
    // A patient chosen or a reason typed in an open booking: closing it by accident would lose them.
    dirty: !!choice && (patientId !== '' || reasonText !== ''),
    reasonCategory,
    channel,
    onReasonCategory: (v: BookingForm['reasonCategory']) => form.setValue('reasonCategory', v),
    onChannel: (v: BookingForm['channel']) => form.setValue('channel', v),
    patient: validId ? { loading: patient.isPending, match: patient.data ?? null } : null,
    chosenPatientId: patientId,
    patientSearch: search.view,
    booking: book.isPending || preview.isPending || bookMany.isPending,
    bookError: book.isError ? errorOf(book.error) : preview.isError ? errorOf(preview.error) : bookMany.isError ? errorOf(bookMany.error) : null,
    repeat,
    maxRepeat,
    unit,
    days,
    onUnit: setUnit,
    onToggleDay: (d: number) => setDays(c => (c.includes(d) ? c.filter(x => x !== d) : [...c, d])),
    onRepeat: (n: number) => setRepeat(Math.min(maxRepeat, Math.max(1, Math.trunc(n) || 1))),
    plan,
    seriesDone,
    onToggleOccurrence: (i: number) => setPlan(p => (p ? toggleRow(p, i) : p)),
    onMoveOccurrence: (i: number, to: { date?: string; time?: string }) => { if (timeZone && (to.date || to.time)) setPlan(p => (p ? moveRow(p, i, to, timeZone) : p)); },
    onBackToForm: () => { setPlan(null); bookMany.reset(); },
    onDismissSeries: () => setSeriesDone(null),
    booked,
    onSlot: (calendarId: string, calendarName: string, start: string) => choose({ calendarId, calendarName, start, visitType: 'FollowUp' }),
    onVisitType: (visitType: VisitType) => choice && choose({ ...choice, visitType }),
    onConfirm: form.handleSubmit(values => (plan ? bookMany.mutate(values) : repeat > 1 ? preview.mutate() : book.mutate(values))),
    onClose: () => {
      if (hold.data) release.mutate(hold.data.id);
      setRebook(null);
      setChoice(null);
      hold.reset();
      book.reset();
      setPlan(null);
      setRepeat(1);
      setUnit('weekly');
      setDays([]);
      form.reset(emptyBooking);
      clearSearch();
    },
    onDismissBooked: () => setBooked(null),
    rebook,
    onRebook: (patient: { id: string; name: string }) => { setBooked(null); setRebook(patient); },
  };
}

export type BookingViewModel = ReturnType<typeof useBookingController>;
