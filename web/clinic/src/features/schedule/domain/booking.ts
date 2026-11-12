import { z } from 'zod';
import type { VisitType } from './schedule';
import { localDate } from './changes';

// Mirrors src/Modules/Scheduling/Medvya.Scheduling.Domain/Appointment.cs and IBookingService.cs (the server is the authority).
export const visitTypes = ['FollowUp', 'NewConsultation', 'Procedure', 'Teleconsult'] as const satisfies readonly VisitType[];
export const reasonCategories = ['General', 'ChronicReview', 'Results', 'PostDischarge', 'Screening', 'Vaccination', 'Paperwork', 'Other'] as const;
export const channels = ['InPerson', 'Phone', 'WhatsApp'] as const;
export type ReasonCategory = (typeof reasonCategories)[number];
export type Channel = (typeof channels)[number];

/** POST /v1/slot-holds answer: the slots are reserved for this user until expiresAt. */
export type Hold = { id: string; facilityId: string; calendarId: string; visitType: VisitType; start: string; end: string; expiresAt: string };
export type BookedAppointment = { id: string; reference: string; patientName: string; mrn: string; calendarId: string; start: string; end: string; state: 'Booked' };
export type PatientMatch = { id: string; fullName: string; mrn: string };

/** The slot the user clicked, and the visit type that decides how many slots the hold covers. */
export type SlotChoice = { calendarId: string; calendarName: string; start: string; visitType: VisitType };

// The patient is chosen with front-desk search (MDV-P1-018); the API takes the chosen opaque id and the panel shows name and MRN.
export const bookingSchema = z.object({
  patientId: z.uuid(),
  reasonCategory: z.enum(reasonCategories),
  reasonText: z.string().trim().max(500),
  channel: z.enum(channels),
});
export type BookingForm = z.infer<typeof bookingSchema>;

export const emptyBooking: BookingForm = { patientId: '', reasonCategory: 'General', reasonText: '', channel: 'InPerson' };

/** 409 codes from the API (SlotUnavailableException); anything else is a generic failure. */
export const slotErrors = ['being_booked', 'slot_unavailable', 'not_bookable', 'in_past', 'hold_expired', 'busy', 'closed', 'terms_not_accepted', 'series_not_booked'] as const;
export type SlotError = (typeof slotErrors)[number] | 'failed';
export const slotError = (code: string | undefined): SlotError =>
  (slotErrors as readonly string[]).includes(code ?? '') ? (code as SlotError) : 'failed';

/**
 * Registering a new patient from the booking panel (MDV-P1-021): the slot travels to registration and back in the address as an
 * opaque calendar id, a time and a visit type (never patient data); registration adds the new patient's opaque id on the way back.
 */
const slotKeys = ['date', 'view', 'calendar', 'start', 'visitType'] as const;

/** The query that opens this slot's day and brings the booking panel back to it. */
export const slotAddress = (choice: SlotChoice, timeZone: string) =>
  new URLSearchParams({ date: localDate(choice.start, timeZone), view: 'day', calendar: choice.calendarId, start: choice.start, visitType: choice.visitType });

/** The slot part of an address (registration carries it along untouched), or null when it names no slot. */
export function pickSlot(params: URLSearchParams): URLSearchParams | null {
  if (!params.get('calendar')) return null;
  return new URLSearchParams(slotKeys.flatMap(k => { const v = params.get(k); return v ? [[k, v]] : []; }));
}

/** The slot and patient to resume booking with, or null when the address does not name a valid one. */
export function readSlotReturn(params: URLSearchParams): (Omit<SlotChoice, 'calendarName'> & { patientId: string }) | null {
  const [calendarId, start, visitType, patientId] = ['calendar', 'start', 'visitType', 'patient'].map(k => params.get(k) ?? '');
  const valid = !!calendarId && z.uuid().safeParse(patientId).success && z.iso.datetime({ offset: true }).safeParse(start).success
    && (visitTypes as readonly string[]).includes(visitType!);
  // The patient was registered on the way, so this is their first visit: the untouched default Follow-up comes back as New consultation (UX-10).
  const type = visitType === 'FollowUp' ? 'NewConsultation' : visitType as VisitType;
  return valid ? { calendarId: calendarId!, start: start!, visitType: type, patientId: patientId! } : null;
}

/** The address once the booking has resumed: the day and view stay, the slot and patient go. */
export function withoutSlotReturn(params: URLSearchParams): URLSearchParams {
  const out = new URLSearchParams(params);
  for (const k of ['calendar', 'start', 'visitType', 'patient']) out.delete(k);
  return out;
}
