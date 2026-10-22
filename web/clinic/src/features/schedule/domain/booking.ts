import { z } from 'zod';
import type { VisitType } from './schedule';

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
export const slotErrors = ['being_booked', 'slot_unavailable', 'not_bookable', 'in_past', 'hold_expired', 'busy'] as const;
export type SlotError = (typeof slotErrors)[number] | 'failed';
export const slotError = (code: string | undefined): SlotError =>
  (slotErrors as readonly string[]).includes(code ?? '') ? (code as SlotError) : 'failed';
