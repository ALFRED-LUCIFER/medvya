import { z } from 'zod';
import type { ReasonCategory } from '../../schedule/domain/booking';
import type { VisitType } from '../../schedule/domain/schedule';

// GET /v1/waitlist (src/Modules/Scheduling/Medvya.Scheduling.Application/IWaitlistService.cs). Names and MRN for the desk, preferences
// and states; no reasons in words. Instants are UTC and shown in the facility's zone; days are the server's DayOfWeek names.
export type WaitlistState = 'Waiting' | 'Offered';
export type SlotMatch = { calendarId: string; calendarName: string; start: string };
export type WaitlistEntry = {
  id: string; patientId: string; patientName: string; mrn: string;
  calendarId: string | null; calendarName: string | null; visitType: VisitType; reasonCategory: ReasonCategory;
  days: DayName[]; from: string | null; until: string | null;
  state: WaitlistState; requestedAt: string;
  /** The slot held for the patient while an offer is open, and until when. */
  offered: SlotMatch | null; offerExpiresAt: string | null;
  /** The earliest free slot that suits a waiting patient: what "Offer" holds. Null when none in the next two weeks. */
  match: SlotMatch | null;
  /** A doctor's follow-up from a consultation (MDV-P2-043): the period chosen and the day from which a slot suits (YYYY-MM-DD). Null: the desk added it. */
  followUp: string | null; dueOn: string | null;
};
/** The visit made when an offer is accepted (the booking answer; the desk sees the patient and the time). */
export type BookedFromWaitlist = { id: string; calendarId: string; start: string; patientName: string };
/** What the desk just did, announced in words: an offer sent, a visit booked. */
export type Outcome = { kind: 'added' | 'offered' | 'declined' | 'removed' } | { kind: 'booked'; visit: BookedFromWaitlist };
export type Waitlist = { facilityId: string; timeZone: string; now: string; entries: WaitlistEntry[] };

/** Monday first, as a week reads at a desk; the values are the server's DayOfWeek names. */
export const weekDays = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'] as const;
export type DayName = (typeof weekDays)[number];

const time = z.union([z.string().regex(/^\d{2}:\d{2}$/), z.literal('')]);

/** The preferences form: the patient is chosen with front-desk search, outside the form. Empty = no preference. */
export const waitlistSchema = z.object({
  calendarId: z.string(),
  visitType: z.enum(['FollowUp', 'NewConsultation', 'Procedure', 'Teleconsult']),
  reasonCategory: z.enum(['General', 'ChronicReview', 'Results', 'PostDischarge', 'Screening', 'Vaccination', 'Paperwork', 'Other']),
  days: z.array(z.enum(weekDays)),
  from: time,
  until: time,
}).superRefine((v, ctx) => {
  if (v.from && v.until && v.until < v.from) ctx.addIssue({ code: 'custom', path: ['until'], message: 'window' });
});
export type WaitlistForm = z.infer<typeof waitlistSchema>;

export const emptyWaitlistForm: WaitlistForm = { calendarId: '', visitType: 'FollowUp', reasonCategory: 'General', days: [], from: '', until: '' };

/** The request body: no preference is null or empty; times go with seconds, as the API reads them. */
export const toRequest = (facilityId: string, patientId: string, v: WaitlistForm) => ({
  facilityId, patientId, calendarId: v.calendarId || null, visitType: v.visitType, reasonCategory: v.reasonCategory,
  days: v.days, from: v.from ? `${v.from}:00` : null, until: v.until ? `${v.until}:00` : null,
});

/** The first entry that "Offer" would be offered to for a slot: oldest request first, as the server lists them. */
export const offerable = (e: WaitlistEntry) => e.state === 'Waiting' && e.match !== null;

/** Codes the screen has a sentence for: field errors arrive as "field:code", 409s as a bare code. */
const known = ['slot_unavailable', 'being_booked', 'not_a_match', 'in_past', 'closed', 'not_bookable', 'busy', 'changed', 'invalid_transition',
  'offer_expired', 'no_offer', 'terms_not_accepted', 'already_waiting', 'unknown', 'before_from'] as const;
export type WaitlistError = (typeof known)[number] | 'failed' | 'forbidden';
export const waitlistError = (code: string | undefined): WaitlistError => {
  const last = code?.split(':').pop() ?? '';
  return (known as readonly string[]).includes(last) ? (last as WaitlistError) : 'failed';
};
