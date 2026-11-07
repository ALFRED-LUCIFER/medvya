import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { cancelSchema, changeError, emptyCancel, rulesFor, type CancelForm } from '../domain/changes';
import type { Appointment } from '../domain/schedule';
import { ApiError, cancelAppointment, cancelFollowing, fetchHistory, fetchMessages, markNoShow, rescheduleAppointment } from '../services/scheduleApi';
import { checkIn } from '../../front-desk/services/frontDeskApi';

/** The visit the desk opened, with the calendar it sits in; `openedAt` is "now" for the action rules. */
export type Opened = { appointment: Appointment; calendarId: string; calendarName: string; openedAt: number };
/** A new slot for the opened visit, picked by click or by drag, waiting for confirmation. */
export type Target = { calendarId: string; calendarName: string; start: string };
export type Mode = 'view' | 'pick' | 'confirmMove' | 'cancel';
export type Done = { kind: 'moved' | 'cancelled' | 'cancelledFollowing' | 'noShow' | 'checkedIn'; appointment: Appointment; calendarName: string; count: number };

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
  const refreshSchedule = () => Promise.all([queryClient.invalidateQueries({ queryKey: ['schedule', facilityId] }), queryClient.invalidateQueries({ queryKey: ['visit-history'] })]);
  const finish = (kind: Done['kind'], calendarName?: string, count = 1) => (appointment: Appointment) => {
    setDone({ kind, appointment, calendarName: calendarName ?? opened!.calendarName, count });
    setMode('view');
    setTarget(null);
    form.reset(emptyCancel);
  };
  const id = () => opened!.appointment.id;
  // What the patient was told and whether it arrived. Polled gently so a send or a receipt shows without reopening the panel.
  const messages = useQuery({
    queryKey: ['messages', facilityId, opened?.appointment.id],
    queryFn: () => fetchMessages(facilityId, opened!.appointment.id),
    enabled: opened !== null,
    refetchInterval: 30_000,
  });

  // Where the visit was before (MDV-P1-024); refetched after each change of this visit.
  const history = useQuery({ queryKey: ['visit-history', opened?.appointment.id], queryFn: () => fetchHistory(opened!.appointment.id), enabled: opened !== null });

  const move = useMutation({
    mutationFn: (t: Target) => rescheduleAppointment(id(), t.calendarId, t.start),
    onSuccess: (a, t) => finish('moved', t.calendarName)(a),
    onSettled: refreshSchedule,
  });
  const cancel = useMutation({ mutationFn: (v: CancelForm) => cancelAppointment(id(), v.reason, v.note), onSuccess: finish('cancelled'), onSettled: refreshSchedule });
  const cancelRest = useMutation({ mutationFn: (v: CancelForm) => cancelFollowing(id(), v.reason, v.note), onSuccess: list => finish('cancelledFollowing', undefined, list.length)({ ...opened!.appointment, state: 'Cancelled' }), onSettled: refreshSchedule });
  const noShow = useMutation({ mutationFn: () => markNoShow(id()), onSuccess: finish('noShow'), onSettled: refreshSchedule });
  const arrive = useMutation({
    mutationFn: () => checkIn(id(), 0), // 0: not timed here; the check-in time metric comes from the board (exit gate 3)
    onSuccess: () => finish('checkedIn')({ ...opened!.appointment, state: 'CheckedIn' }),
    onSettled: refreshSchedule,
  });
  const mutations = [move, cancel, cancelRest, noShow, arrive];
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
    messages: messages.isError ? ('error' as const) : (messages.data ?? ('loading' as const)),
    history: history.isError ? ('error' as const) : (history.data ?? ('loading' as const)),
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
    onConfirmCancelFollowing: form.handleSubmit(values => cancelRest.mutate(values)),
    onNoShow: () => noShow.mutate(),
    onCheckIn: () => arrive.mutate(),
    onBack: () => { reset(); setTarget(null); setMode('view'); },
  };
}

export type AppointmentViewModel = ReturnType<typeof useAppointmentController>;
