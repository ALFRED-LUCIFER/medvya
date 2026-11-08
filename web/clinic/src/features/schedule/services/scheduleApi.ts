import { apiFetch } from '../../api/services/apiClient';
import type { BookedAppointment, BookingForm, Hold, PatientMatch, SlotChoice } from '../domain/booking';
import type { Occurrence, SeriesResult } from '../domain/series';
import type { CancelReason, VisitMove } from '../domain/changes';
import type { PatientMessage } from '../domain/messages';
import type { SaveResult, SavedAdjustment } from '../domain/adjustments';
import type { Appointment, ScheduleDay } from '../domain/schedule';
import type { ScheduleWeek } from '../domain/views';
import type { DaySheet } from '../domain/daySheet';

/** An API answer other than 2xx. `status` tells 403 from other failures; `code` is the 409 reason (e.g. "being_booked"). */
export class ApiError extends Error {
  readonly status: number;
  readonly code: string | undefined;
  /** The problem-details body, for answers that carry data (e.g. the visits an "actions_required" leave affects). */
  readonly body: unknown;
  constructor(status: number, code?: string, body?: unknown) {
    super(`API ${status}`);
    this.status = status;
    this.code = code;
    this.body = body;
  }
}

// State-changing calls carry the CSRF header the BFF requires (ADR-004).
export const csrf = { 'X-Medvya-Csrf': '1' };

export async function send<T>(url: string, init: RequestInit = {}): Promise<T> {
  const res = await apiFetch(url, { credentials: 'same-origin', ...init });
  if (!res.ok) {
    const body = (await res.json().catch(() => null)) as { code?: string; errors?: Record<string, string[]> } | null;
    // A rejected field (400 problem details) is reported as "field:code" so a screen can name what was wrong.
    const rejected = Object.entries(body?.errors ?? {}).find(([, codes]) => codes.length > 0);
    throw new ApiError(res.status, body?.code ?? (rejected ? `${rejected[0]}:${rejected[1][0]}` : undefined), body);
  }
  return (res.status === 204 ? undefined : await res.json()) as T;
}

/** With a `key` the call carries an Idempotency-Key, so the shared client may repeat it after a timeout without acting twice. */
export const post = <T>(url: string, body: unknown, key?: string) =>
  send<T>(url, { method: 'POST', headers: { ...csrf, 'Content-Type': 'application/json', ...(key ? { 'Idempotency-Key': key } : {}) }, body: JSON.stringify(body) });

/** The facility's day view; without a date the server picks today in the facility's time zone. Ids and dates only in the URL. */
export function fetchScheduleDay(facilityId: string, date: string | null): Promise<ScheduleDay> {
  const query = new URLSearchParams({ facilityId, ...(date ? { date } : {}) });
  return send(`/v1/schedule?${query}`);
}

/** Monday to Sunday of one doctor or resource around the date (this week when null). */
export function fetchScheduleWeek(facilityId: string, calendarId: string, date: string | null): Promise<ScheduleWeek> {
  const query = new URLSearchParams({ facilityId, calendarId, ...(date ? { date } : {}) });
  return send(`/v1/schedule/week?${query}`);
}

export const holdSlot = (facilityId: string, s: SlotChoice) =>
  post<Hold>('/v1/slot-holds', { facilityId, calendarId: s.calendarId, start: s.start, visitType: s.visitType });

export const releaseHold = (holdId: string) => send<void>(`/v1/slot-holds/${holdId}`, { method: 'DELETE', headers: csrf });

/** The free-text reason goes in the body only, never in a URL. */
export const bookAppointment = (holdId: string, form: BookingForm, key: string) => post<BookedAppointment>('/v1/appointments', { holdId, ...form }, key);

/** Name and MRN to confirm the patient before booking (audited by the server); null when there is no such patient. */
export async function findPatient(facilityId: string, patientId: string): Promise<PatientMatch | null> {
  try {
    return await send<PatientMatch>(`/v1/schedule/patients/${patientId}?${new URLSearchParams({ facilityId })}`);
  } catch (e) {
    if (e instanceof ApiError && e.status === 404) return null;
    throw e;
  }
}

// MDV-P1-024: changing a booking. Ids in the path, the new slot or the reason in the body; never patient data in the URL.
export const rescheduleAppointment = (appointmentId: string, calendarId: string, start: string) =>
  post<Appointment>(`/v1/appointments/${appointmentId}/reschedule`, { calendarId, start });

export const cancelAppointment = (appointmentId: string, reason: CancelReason, note: string) =>
  post<Appointment>(`/v1/appointments/${appointmentId}/cancel`, { reason, note: note || null });

/** MDV-P1-026: "this and following" cancels the rest of the series with one reason. */
export const cancelFollowing = (appointmentId: string, reason: CancelReason, note: string) =>
  post<Appointment[]>(`/v1/appointments/${appointmentId}/cancel-following`, { reason, note: note || null });

// MDV-P1-026: recurring appointments. The preview lists each occurrence with the conflict that would refuse it; booking takes the starts the desk confirmed.
export const previewSeries = (facilityId: string, s: { calendarId: string; visitType: string }, firstStart: string, count: number, monthly: boolean, weekdays: number[]) =>
  post<Occurrence[]>('/v1/appointments/series/preview', { facilityId, calendarId: s.calendarId, visitType: s.visitType, firstStart, count, intervalWeeks: 1, monthly, weekdays });

export const bookSeries = (facilityId: string, s: { calendarId: string; visitType: string }, form: BookingForm, starts: string[]) =>
  post<SeriesResult>('/v1/appointments/series', { facilityId, calendarId: s.calendarId, visitType: s.visitType, starts, ...form });

export const markNoShow = (appointmentId: string) => post<Appointment>(`/v1/appointments/${appointmentId}/no-show`, {});

/** The visit's moves, oldest first (MDV-P1-024). */
export const fetchHistory = (appointmentId: string) => send<VisitMove[]>(`/v1/appointments/${appointmentId}/history`);

/** Messages sent to the patient about a visit (statuses only); `preparing` while the booking's messages are not queued yet. Ids only in the URL. */
export const fetchMessages = (facilityId: string, appointmentId: string) =>
  send<{ messages: PatientMessage[]; preparing: boolean }>(`/v1/appointments/${appointmentId}/notifications?facilityId=${facilityId}`);

// MDV-P1-012: leave and one-off sessions. An answer of 409 "actions_required" carries the affected visits (ApiError.body.affected).
export const fetchAdjustments = (facilityId: string) => send<SavedAdjustment[]>(`/v1/schedule/exceptions?${new URLSearchParams({ facilityId })}`);

export const saveAdjustment = (body: unknown) => post<SaveResult>('/v1/schedule/exceptions', body);

/** Edit takes the body of an add (same action rules); delete cancels the exception and gives the slots back. */
export const updateAdjustment = (id: string, body: unknown) => send<SaveResult>(`/v1/schedule/exceptions/${id}`, { method: 'PUT', headers: { ...csrf, 'Content-Type': 'application/json' }, body: JSON.stringify(body) });

export const deleteAdjustment = (facilityId: string, id: string, version: string) =>
  send<void>(`/v1/schedule/exceptions/${id}?${new URLSearchParams({ facilityId, version })}`, { method: 'DELETE', headers: csrf });

/** MDV-P1-050: the server audits the print and answers with the only fields that may go on paper. */
export const printDaySheet = (facilityId: string, date: string | null) => post<DaySheet>('/v1/schedule/print', { facilityId, date });
