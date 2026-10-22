import type { BookedAppointment, BookingForm, Hold, PatientMatch, SlotChoice } from '../domain/booking';
import type { ScheduleDay } from '../domain/schedule';

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
