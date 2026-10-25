import type { BookedAppointment, BookingForm, Hold, PatientMatch, SlotChoice } from '../domain/booking';
import type { CancelReason } from '../domain/changes';
import type { Appointment, ScheduleDay } from '../domain/schedule';
import type { ScheduleWeek } from '../domain/views';

/** An API answer other than 2xx. `status` tells 403 from other failures; `code` is the 409 reason (e.g. "being_booked"). */
export class ApiError extends Error {
  readonly status: number;
  readonly code: string | undefined;
  constructor(status: number, code?: string) {
    super(`API ${status}`);
    this.status = status;
    this.code = code;
  }
}

// State-changing calls carry the CSRF header the BFF requires (ADR-004).
export const csrf = { 'X-Medvya-Csrf': '1' };

export async function send<T>(url: string, init: RequestInit = {}): Promise<T> {
  const res = await fetch(url, { credentials: 'same-origin', ...init });
  if (!res.ok) {
    const body = (await res.json().catch(() => null)) as { code?: string } | null;
    throw new ApiError(res.status, body?.code);
  }
  return (res.status === 204 ? undefined : await res.json()) as T;
}

export const post = <T>(url: string, body: unknown) =>
  send<T>(url, { method: 'POST', headers: { ...csrf, 'Content-Type': 'application/json' }, body: JSON.stringify(body) });

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
export const bookAppointment = (holdId: string, form: BookingForm) => post<BookedAppointment>('/v1/appointments', { holdId, ...form });

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

export const markNoShow = (appointmentId: string) => post<Appointment>(`/v1/appointments/${appointmentId}/no-show`, {});
