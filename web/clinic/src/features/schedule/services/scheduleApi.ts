import type { ScheduleDay } from '../domain/schedule';

/** An API answer other than 2xx; the status lets controllers tell 403 (no access) from other failures. */
export class ApiError extends Error {
  readonly status: number;
  constructor(status: number) {
    super(`API ${status}`);
    this.status = status;
  }
}

/** The facility's day view; without a date the server picks today in the facility's time zone. Ids and dates only in the URL. */
export async function fetchScheduleDay(facilityId: string, date: string | null): Promise<ScheduleDay> {
  const query = new URLSearchParams({ facilityId, ...(date ? { date } : {}) });
  const res = await fetch(`/v1/schedule?${query}`, { credentials: 'same-origin' });
  if (!res.ok) throw new ApiError(res.status);
  return res.json() as Promise<ScheduleDay>;
}
