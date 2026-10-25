import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { cancelSchema, changeError, emptyCancel, rulesFor, type CancelForm } from '../domain/changes';
import type { Appointment } from '../domain/schedule';
import { ApiError, cancelAppointment, markNoShow, rescheduleAppointment } from '../services/scheduleApi';
import { checkIn } from '../../front-desk/services/frontDeskApi';

/** The visit the desk opened, with the calendar it sits in; `openedAt` is "now" for the action rules. */
export type Opened = { appointment: Appointment; calendarId: string; calendarName: string; openedAt: number };
/** A new slot for the opened visit, picked by click or by drag, waiting for confirmation. */
export type Target = { calendarId: string; calendarName: string; start: string };
export type Mode = 'view' | 'pick' | 'confirmMove' | 'cancel';
export type Done = { kind: 'moved' | 'cancelled' | 'noShow' | 'checkedIn'; appointment: Appointment };

/**
 * MDV-P1-024: the appointment panel. Opening a visit shows its actions under the state rules; Reschedule picks a new slot
 * (or a drag drops it there) and asks before moving; Cancel asks for a reason; a no-show offers a rebook. Every change goes
 * through the API, which enforces the same rules. Success or refusal, the schedule refreshes, so the desk sees what changed.
 */
export function useAppointmentController(facilityId: string, access: { timeZone: string | null; canBook: boolean; canCheckIn: boolean }) {
  const queryClient = useQueryClient();
  const [opened, setOpened] = useState<Opened | null>(null);
  const [mode, setMode] = useState<Mode>('view');
  const [target, setTarget] = useState<Target | null>(null);
  const [done, setDone] = useState<Done | null>(null);
  const form = useForm<CancelForm>({ resolver: zodResolver(cancelSchema), defaultValues: emptyCancel });
  const refreshSchedule = () => queryClient.invalidateQueries({ queryKey: ['schedule', facilityId] });
  const finish = (kind: Done['kind']) => (appointment: Appointment) => {
    setDone({ kind, appointment });
    setMode('view');
    setTarget(null);
    form.reset(emptyCancel);
  };
  const id = () => opened!.appointment.id;

  const move = useMutation({ mutationFn: (t: Target) => rescheduleAppointment(id(), t.calendarId, t.start), onSuccess: finish('moved'), onSettled: refreshSchedule });
  const cancel = useMutation({ mutationFn: (v: CancelForm) => cancelAppointment(id(), v.reason, v.note), onSuccess: finish('cancelled'), onSettled: refreshSchedule });
  const noShow = useMutation({ mutationFn: () => markNoShow(id()), onSuccess: finish('noShow'), onSettled: refreshSchedule });
  const arrive = useMutation({
    mutationFn: () => checkIn(id(), 0), // 0: not timed here; the check-in time metric comes from the board (exit gate 3)
    onSuccess: () => finish('checkedIn')({ ...opened!.appointment, state: 'CheckedIn' }),
    onSettled: refreshSchedule,
  });
  const mutations = [move, cancel, noShow, arrive];
  const failed = mutations.find(m => m.isError);
  const reset = () => mutations.forEach(m => m.reset());

  const open = (next: Opened | null) => {
    setOpened(next);
    setMode('view');
    setTarget(null);
    setDone(null);
    form.reset(emptyCancel);
    reset();
  };

  return {
    opened,
    mode,
    target,
    done,
    rules: opened && access.timeZone ? rulesFor(opened.appointment, { now: opened.openedAt, timeZone: access.timeZone, canBook: access.canBook, canCheckIn: access.canCheckIn }) : null,
    working: mutations.some(m => m.isPending),
    error: failed ? changeError(failed.error instanceof ApiError ? failed.error.code : undefined) : null,
    form,
    onOpen: (appointment: Appointment, calendarId: string, calendarName: string) => open({ appointment, calendarId, calendarName, openedAt: Date.now() }),
    onClose: () => open(null),
    onReschedule: () => { reset(); setMode('pick'); },
    /** A slot clicked while picking, or the visit dropped on a slot: ask before moving. */
    onTarget: (calendarId: string, calendarName: string, start: string) => { reset(); setTarget({ calendarId, calendarName, start }); setMode('confirmMove'); },
    onConfirmMove: () => { if (target) move.mutate(target); },
    onStartCancel: () => { reset(); setMode('cancel'); },
    onReason: (reason: CancelForm['reason']) => form.setValue('reason', reason, { shouldValidate: form.formState.isSubmitted }),
    onConfirmCancel: form.handleSubmit(values => cancel.mutate(values)),
    onNoShow: () => noShow.mutate(),
    onCheckIn: () => arrive.mutate(),
    onBack: () => { reset(); setTarget(null); setMode('view'); },
  };
}

export type AppointmentViewModel = ReturnType<typeof useAppointmentController>;
