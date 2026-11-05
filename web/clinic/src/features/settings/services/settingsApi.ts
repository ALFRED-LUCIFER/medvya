import { ApiError, csrf, post, send } from '../../schedule/services/scheduleApi';
import type { CalendarTemplate, Closure, ClosureForm, ClosureWithVisits, DayHours, Location, LocationForm, LogoLink, Profile, ProfileForm, TermsStatus } from '../domain/settings';

const json = (method: string, body: unknown) => ({ method, headers: { ...csrf, 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
const blank = <T extends Record<string, string>>(form: T) => Object.fromEntries(Object.entries(form).map(([k, v]) => [k, v === '' ? null : v]));

// Practice settings (F-08, MDV-P1-005). Ids in paths, details in bodies; business details only.
export const fetchProfile = () => send<Profile>('/v1/settings/profile');
export const saveProfile = (form: ProfileForm) => send<Profile>('/v1/settings/profile', json('PUT', blank(form)));

/** A short-lived signed link to the logo; null when there is none. */
export async function fetchLogoLink(): Promise<LogoLink | null> {
  try {
    return await send<LogoLink>('/v1/settings/logo');
  } catch (e) {
    if (e instanceof ApiError && e.status === 404) return null;
    throw e;
  }
}
/** The body is the image itself; the server checks the file's real type and size. */
export const uploadLogo = (file: File) => send<void>('/v1/settings/logo', { method: 'PUT', headers: { ...csrf, 'Content-Type': file.type }, body: file });
export const removeLogo = () => send<void>('/v1/settings/logo', { method: 'DELETE', headers: csrf });

export const fetchLocations = () => send<Location[]>('/v1/settings/locations');
export const addLocation = (form: LocationForm) => post<Location>('/v1/settings/locations', blank(form));
export const saveLocation = (id: string, form: LocationForm) => send<Location>(`/v1/settings/locations/${id}`, json('PUT', blank(form)));

export const fetchHours = (locationId: string) => send<DayHours[]>(`/v1/settings/locations/${locationId}/hours`);
export const saveHours = (locationId: string, hours: DayHours[]) => send<DayHours[]>(`/v1/settings/locations/${locationId}/hours`, json('PUT', hours));

export const fetchClosures = (locationId: string) => send<ClosureWithVisits[]>(`/v1/settings/locations/${locationId}/closures`);
export const addClosure = (locationId: string, form: ClosureForm) => post<Closure>(`/v1/settings/locations/${locationId}/closures`, form);
export const removeClosure = (id: string) => send<void>(`/v1/settings/closures/${id}`, { method: 'DELETE', headers: csrf });

// Existing scheduling endpoints (MDV-P1-011): the calendars of a location with their weekly templates, and rooms.
export const fetchTemplates = (facilityId: string) => send<CalendarTemplate[]>(`/v1/schedule/templates?${new URLSearchParams({ facilityId })}`);
export const saveTemplate = (body: unknown) => send<CalendarTemplate>('/v1/schedule/templates', json('PUT', body));
export const addResource = (facilityId: string, name: string) => post<{ id: string; facilityId: string; name: string }>('/v1/schedule/resources', { facilityId, name });

export const fetchTerms = () => send<TermsStatus>('/v1/settings/terms');
/** Only the practice owner may accept; the server answers 403 to anyone else. */
export const acceptTerms = () => post<TermsStatus>('/v1/settings/terms/accept', {});
