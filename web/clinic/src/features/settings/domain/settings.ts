import { z } from 'zod';
import type { MessageKey } from '@medvya/i18n';

// Mirrors src/Modules/Tenancy/Medvya.Tenancy.Application/IPracticeSettings.cs (the server is the authority; these checks only save a round trip).
export type Profile = {
  name: string;
  address: string | null;
  registrationNumber: string | null;
  /** ISO date (yyyy-mm-dd), the registration's last valid day. */
  registrationExpiresOn?: string | null;
  taxNumber: string | null;
  phone: string | null;
  email: string | null;
  hasLogo: boolean;
  /** The row version this was read at (0 before a profile exists); a save sends it back and is refused (409 "changed") if it moved on (#889). */
  version: number;
};
export type LogoLink = { url: string; expiresAt: string };
export type Location = { id: string; name: string; address: string | null; timeZone: string };
/** Weekday by name (the API writes System.DayOfWeek as text); times are "HH:mm:ss" facility wall-clock times. */
export type DayHours = { weekday: Weekday; opens: string; closes: string };
export type Closure = { id: string; facilityId: string; date: string; name: string };
/** A booked visit on a closed date: a reference and a time, never a patient. */
export type VisitToMove = { id: string; reference: string; calendarName: string; visitType: string; start: string; end: string };
export type ClosureWithVisits = { closure: Closure; appointmentsToMove: VisitToMove[] };

/** Monday first, as the week is shown. */
export const weekdays = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'] as const;
export type Weekday = (typeof weekdays)[number];

/** The time zones of the two countries Medvya runs in (ADR-003). */
export const timeZones = ['Asia/Kolkata', 'Asia/Dubai'] as const;

const optional = (max: number) => z.string().trim().max(max);

export const profileSchema = z.object({
  name: z.string().trim().min(1).max(200),
  address: optional(500),
  registrationNumber: optional(50),
  registrationExpiresOn: z.union([z.literal(''), z.iso.date()]),
  taxNumber: optional(30),
  phone: optional(30).refine(v => v === '' || /^[0-9+() -]{6,30}$/.test(v)),
  email: optional(254).refine(v => v === '' || /^[^\s@<>]+@[^\s@<>]+\.[^\s@<>]+$/.test(v)),
});
export type ProfileForm = z.infer<typeof profileSchema>;

export const toProfileForm = (p: Profile): ProfileForm => ({
  name: p.name, address: p.address ?? '', registrationNumber: p.registrationNumber ?? '', registrationExpiresOn: p.registrationExpiresOn ?? '', taxNumber: p.taxNumber ?? '', phone: p.phone ?? '', email: p.email ?? '',
});

export const locationSchema = z.object({ name: z.string().trim().min(1).max(200), address: optional(500), timeZone: z.string().min(1) });
export type LocationForm = z.infer<typeof locationSchema>;
export const emptyLocation: LocationForm = { name: '', address: '', timeZone: timeZones[0] };
/** Adding a location also needs its tax number (GSTIN or TRN) and certificate; the controller asks for them on add only, and the server checks the format. */
export const addLocationSchema = locationSchema.extend({ taxNumber: optional(20) });
export type AddLocationForm = z.infer<typeof addLocationSchema>;
export const emptyAddLocation: AddLocationForm = { ...emptyLocation, taxNumber: '' };

export const certificateMaxBytes = 5 * 1024 * 1024;
export type CertificateProblem = 'required' | 'type' | 'too_large';
export const certificateErrorKey = (p: CertificateProblem): MessageKey => locationErrorKeys[`certificate:${p}`];
/** Quick check before upload; the server decides from the file's own bytes and is never bypassed. */
export const certificateProblem = (file: { type: string; size: number } | null): CertificateProblem | null =>
  !file || file.size === 0 ? 'required' : !['application/pdf', 'image/png', 'image/jpeg'].includes(file.type) ? 'type' : file.size > certificateMaxBytes ? 'too_large' : null;

/** Where a server refusal of an added location belongs: under a field, or (a wrong country, anything else) beside the button. */
export const locationErrorKeys = {
  'taxNumber:required': 'settings.locations.error.taxNumber.required', 'taxNumber:format': 'settings.locations.error.taxNumber.format',
  'certificate:required': 'settings.locations.error.certificate.required', 'certificate:type': 'settings.locations.error.certificate.type',
  'certificate:too_large': 'settings.locations.error.certificate.too_large', 'countryCode:mismatch': 'settings.locations.error.country',
} as const satisfies Record<string, MessageKey>;
export const locationProblem = (code: string | undefined): { field: 'taxNumber' | 'certificate' | 'form'; key: MessageKey } => {
  const key = (locationErrorKeys as Record<string, MessageKey>)[code ?? ''];
  const field = (code ?? '').split(':')[0];
  return key ? { field: field === 'taxNumber' || field === 'certificate' ? field : 'form', key } : { field: 'form', key: 'settings.saveFailed' };
};

export const closureSchema = z.object({ date: z.iso.date(), name: z.string().trim().min(1).max(100) });
export type ClosureForm = z.infer<typeof closureSchema>;

export const resourceSchema = z.object({ name: z.string().trim().min(1).max(100) });
export type ResourceForm = z.infer<typeof resourceSchema>;

/** One weekday row of the Hours tab: closed days have no row on the server. */
export type HoursRow = { weekday: Weekday; open: boolean; opens: string; closes: string };

const hhmm = (t: string) => t.slice(0, 5);
const defaultOpens = '09:00', defaultCloses = '17:00';

export function toHoursRows(hours: DayHours[]): HoursRow[] {
  return weekdays.map(weekday => {
    const h = hours.find(x => x.weekday === weekday);
    return { weekday, open: !!h, opens: h ? hhmm(h.opens) : defaultOpens, closes: h ? hhmm(h.closes) : defaultCloses };
  });
}

/** Open days only; an open day whose closing time is not after its opening time makes the whole list invalid (null). */
export function toDayHours(rows: HoursRow[]): DayHours[] | null {
  const open = rows.filter(r => r.open);
  if (open.some(r => !r.opens || !r.closes || r.opens >= r.closes)) return null;
  return open.map(r => ({ weekday: r.weekday, opens: `${r.opens}:00`, closes: `${r.closes}:00` }));
}

export function hoursProblems(rows: HoursRow[]): Weekday[] {
  return rows.filter(r => r.open && (!r.opens || !r.closes || r.opens >= r.closes)).map(r => r.weekday);
}

// ---- Bookable schedules (the weekly template of a doctor or resource, MDV-P1-011) ----

export type TemplateHours = { weekday: Weekday; start: string; end: string; room: string | null };
export type TemplateBreak = { weekday: Weekday; start: string; end: string; label: string | null };
export type TemplateLength = { visitType: string; minutes: number };
export type CalendarTemplate = {
  calendarId: string;
  calendarKind: 'Doctor' | 'Resource';
  name: string;
  slotMinutes: number | null;
  hours: TemplateHours[];
  breaks: TemplateBreak[];
  visitLengths: TemplateLength[];
  appointmentsOutsideHours: { id: string; reference: string; start: string; end: string }[];
};

/** A session being edited: times as "HH:mm", the room kept from the saved template so saving here never drops it. */
export type SessionRow = { weekday: Weekday; start: string; end: string; room: string | null };

export const slotLengths = [10, 15, 20, 30, 45, 60] as const;

export const toSessionRows = (hours: TemplateHours[]): SessionRow[] => hours.map(h => ({ weekday: h.weekday, start: hhmm(h.start), end: hhmm(h.end), room: h.room }));

/** Sessions that end before they start, or overlap another session on the same day (null: fine). */
export function sessionProblem(rows: SessionRow[]): 'order' | 'overlap' | null {
  if (rows.some(r => !r.start || !r.end || r.start >= r.end)) return 'order';
  const overlap = rows.some((a, i) => rows.some((b, j) => i < j && a.weekday === b.weekday && a.start < b.end && b.start < a.end));
  return overlap ? 'overlap' : null;
}

/** The body of PUT /v1/schedule/templates: edited sessions and slot length, with breaks and visit lengths exactly as saved. */
export const templateBody = (facilityId: string, t: CalendarTemplate, slotMinutes: number, sessions: SessionRow[]) => ({
  facilityId, calendarId: t.calendarId, calendarKind: t.calendarKind, slotMinutes,
  hours: sessions.map(s => ({ weekday: s.weekday, start: `${s.start}:00`, end: `${s.end}:00`, room: s.room })),
  breaks: t.breaks, visitLengths: t.visitLengths,
});

// ---- Logo ----

export const logoMaxBytes = 512 * 1024;
export type LogoProblem = 'type' | 'size';
/** Quick check before upload; the server decides from the file's own bytes and is never bypassed. */
export const logoProblem = (file: { type: string; size: number }): LogoProblem | null =>
  !['image/png', 'image/jpeg'].includes(file.type) ? 'type' : file.size === 0 || file.size > logoMaxBytes ? 'size' : null;

/** Problem codes from the API ("field:code" for a rejected field, as the shared client reports them). */
export const knownErrors = ['logo:type', 'logo:too_large', 'date:duplicate', 'name:duplicate', 'name:link'] as const;
export type SettingsError = (typeof knownErrors)[number] | 'failed';
export const settingsError = (code: string | undefined): SettingsError =>
  (knownErrors as readonly string[]).includes(code ?? '') ? (code as SettingsError) : 'failed';

/** The tenant's locations and the one a tab is looking at. */
export type LocationChoice = { locations: Location[]; locationId: string; onLocation: (id: string) => void; locationsLoading: boolean; locationsFailed: boolean };

export const settingsTabs = [
  'profile', 'locations', 'hours', 'holidays', 'displays', 'metrics', 'terms',
  'staff', 'doctors',
  'nurse', 'medicines',
  'notifications',
  'requests', 'policy', 'review', 'audit', 'data',
] as const;
export type SettingsTab = (typeof settingsTabs)[number];

/** Settings in five groups (MDV audit UX-04), each tab one subject; the tab id stays the address (?tab=hours), so old links keep working. */
export const settingsGroups = {
  practice: ['profile', 'locations', 'hours', 'holidays', 'displays', 'metrics', 'terms'],
  people: ['staff', 'doctors'],
  clinical: ['nurse', 'medicines'],
  messages: ['notifications'],
  security: ['requests', 'policy', 'review', 'audit', 'data'],
} as const satisfies Record<string, readonly SettingsTab[]>;
export type SettingsGroup = keyof typeof settingsGroups;
export const settingsGroupIds = Object.keys(settingsGroups) as SettingsGroup[];
export const groupOf = (tab: SettingsTab): SettingsGroup =>
  settingsGroupIds.find(g => (settingsGroups[g] as readonly SettingsTab[]).includes(tab))!;

/** The practice's acceptance of the Terms of Service and DPA (MDV-P1-019). Until accepted, booking and sending are refused by the server. */
/** Where the owner accepts the terms; booking errors and the owner's banner link here. */
export const termsPath = '/settings?tab=terms';
export type TermsStatus = { version: string; accepted: boolean; acceptedAt: string | null; required: boolean };
