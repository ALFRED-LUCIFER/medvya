import { z } from 'zod';
import type { Appointment } from './schedule';

// MDV-P1-024: what the desk may do with a booked visit. Mirrors VisitFlow in src/Modules/Scheduling/Medvya.Scheduling.Domain/Appointment.cs;
// the server is the authority and refuses anything these rules would hide.

export const cancelReasons = ['PatientRequest', 'PatientUnwell', 'DoctorUnavailable', 'BookedInError', 'ClinicClosed', 'Other'] as const;
export type CancelReason = (typeof cancelReasons)[number];

/** No default reason: cancelling always takes a deliberate choice. A note is required for Other and never over 500 characters. */
export const cancelSchema = z
  .object({ reason: z.enum(cancelReasons, { message: 'reason' }), note: z.string().trim().max(500, { message: 'noteLong' }) })
  .refine(v => v.reason !== 'Other' || v.note.length > 0, { path: ['note'], message: 'note' });
export type CancelForm = z.infer<typeof cancelSchema>;
export const emptyCancel = { note: '' } as Partial<CancelForm>;

export type Action = 'checkIn' | 'reschedule' | 'cancel' | 'noShow';
export const actions: readonly Action[] = ['checkIn', 'reschedule', 'cancel', 'noShow'];
/** Why an action is not offered; each is a sentence the panel shows beside the disabled button. */
export type Why = 'noAccess' | 'noCheckIn' | 'onlyBooked' | 'notToday' | 'notStarted' | 'inVisit' | 'completed' | 'cancelled' | 'noShow';
export type Rule = { allowed: true } | { allowed: false; why: Why };

/** The facility-local calendar date (YYYY-MM-DD) of an instant. */
export const localDate = (iso: string, timeZone: string) =>
  new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date(iso));

const ended = (a: Appointment): Why | null =>
  a.state === 'Done' ? 'completed' : a.state === 'Cancelled' ? 'cancelled' : a.state === 'NoShow' ? 'noShow' : null;

/**
 * Each action for this visit, allowed or with the reason it is not. `now` is when the desk opened the visit; check-in needs
 * Check-in & queue access, the other actions Register / book access.
 */
export function rulesFor(a: Appointment, ctx: { now: number; timeZone: string; canBook: boolean; canCheckIn: boolean }): Record<Action, Rule> {
  const no = (why: Why): Rule => ({ allowed: false, why });
  const yes: Rule = { allowed: true };
  const booked = a.state === 'Booked';
  const over = ended(a);
  return {
    checkIn: !ctx.canCheckIn ? no('noCheckIn') : !booked ? no(over ?? 'onlyBooked')
      : localDate(a.start, ctx.timeZone) !== localDate(new Date(ctx.now).toISOString(), ctx.timeZone) ? no('notToday') : yes,
    reschedule: !ctx.canBook ? no('noAccess') : booked ? yes : no(over ?? 'onlyBooked'),
    cancel: !ctx.canBook ? no('noAccess') : booked || a.state === 'CheckedIn' ? yes : no(over ?? 'inVisit'),
    noShow: !ctx.canBook ? no('noAccess') : !booked ? no(over ?? 'onlyBooked') : Date.parse(a.start) > ctx.now ? no('notStarted') : yes,
  };
}

/** 409 codes of the change endpoints: visit moves (VisitTransitionException) and slot answers (SlotUnavailableException). */
export const changeErrors = ['changed', 'invalid_transition', 'not_started', 'not_today', 'slot_unavailable', 'being_booked', 'not_bookable', 'in_past', 'busy'] as const;
export type ChangeError = (typeof changeErrors)[number] | 'failed';
export const changeError = (code: string | undefined): ChangeError =>
  (changeErrors as readonly string[]).includes(code ?? '') ? (code as ChangeError) : 'failed';
