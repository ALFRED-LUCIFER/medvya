import { csrf, post, send } from '../../schedule/services/scheduleApi';
import { toRequest, type BookedFromWaitlist, type Waitlist, type WaitlistEntry, type WaitlistForm } from '../domain/waitlist';

/** The facility's waiting and offered patients with the free slot each could be offered. Ids only in the URL. */
export const fetchWaitlist = (facilityId: string) => send<Waitlist>(`/v1/waitlist?${new URLSearchParams({ facilityId })}`);

export const addToWaitlist = (facilityId: string, patientId: string, form: WaitlistForm) => post<WaitlistEntry>('/v1/waitlist', toRequest(facilityId, patientId, form));

/** Holds the slot the desk saw and messages the patient (no clinical details). */
export const offerSlot = (id: string, calendarId: string, start: string) => post<WaitlistEntry>(`/v1/waitlist/${id}/offer`, { calendarId, start });

export const declineOffer = (id: string) => post<WaitlistEntry>(`/v1/waitlist/${id}/decline`, {});

/** The patient said yes: books the held slot. */
export const acceptOffer = (id: string) => post<BookedFromWaitlist>(`/v1/waitlist/${id}/accept`, {});

export const removeFromWaitlist = (id: string) => send<void>(`/v1/waitlist/${id}`, { method: 'DELETE', headers: csrf });
