import { catalogs, type MessageKey } from '@medvya/i18n';
import type { Role } from '../../session/domain/session';
// Mirrors src/Host.Api/AuditLog/AccessLogEndpoints.cs (the server is the authority). Rows hold ids and codes; the patient's name arrives only
// on the per-patient request, so a list without a chosen patient shows the MRN alone.

export type AuditOutcome = 'Allowed' | 'Denied' | 'NotFound' | 'Rejected';

export type AuditRow = {
  id: string;
  occurredAt: string;
  actorUserId: string | null;
  actorName: string | null;
  /** The role held when the event happened; null on rows from before it was recorded. */
  role: Role | null;
  /** The location where the record was read or changed; null on rows from before it was recorded. */
  location: string | null;
  action: string;
  resourceType: string;
  patientId: string | null;
  patientMrn: string | null;
  patientName: string | null;
  purpose: string;
  outcome: AuditOutcome;
  /** Codes and ids only, e.g. the filters of an export. */
  detail: string | null;
};

export type AuditPage = { items: AuditRow[]; next: string | null; actions: string[] };

/** Dates are 'YYYY-MM-DD' from the date inputs, read as days in `zone` (the clinic's time zone); 'all' is the "everyone / any action" choice of a select. */
export type AuditFilters = { actor: string; action: string; from: string; to: string; patientId: string | null; zone: string };

export const all = 'all';
/** The action filter's default (MDV audit UX-13): events about a patient's record only, so settings page views do not push them down. */
export const patientRecords = 'patientRecords';

/**
 * The words for an action code (MDV-P1-052), or null for a code the catalog does not know yet: the screen then shows the code
 * itself rather than nothing. Codes stay codes in the CSV export and the filter's value.
 */
export function actionKey(code: string): MessageKey | null {
  const key = `audit.actionName.${code}`;
  return key in catalogs.en ? (key as MessageKey) : null;
}

const DAY_MS = 86_400_000;

/** The calendar date ('YYYY-MM-DD') of an instant in a time zone. */
export const dateIn = (at: Date, zone: string) => new Intl.DateTimeFormat('en-CA', { timeZone: zone, year: 'numeric', month: '2-digit', day: '2-digit' }).format(at);

/** The instant at 00:00 on a 'YYYY-MM-DD' day in a time zone (the zone's own offset on that day, so daylight saving is respected). */
export function startOfDayIn(day: string, zone: string): Date {
  const [y, m, d] = day.split('-').map(Number) as [number, number, number];
  const wallClockAsUtc = Date.UTC(y, m - 1, d);
  const offsetAt = (t: number) => {
    const p = Object.fromEntries(new Intl.DateTimeFormat('en-US', { timeZone: zone, hourCycle: 'h23', year: 'numeric', month: 'numeric', day: 'numeric', hour: 'numeric', minute: 'numeric', second: 'numeric' })
      .formatToParts(new Date(t)).map(x => [x.type, Number(x.value)]));
    return Date.UTC(p.year!, p.month! - 1, p.day!, p.hour!, p.minute!, p.second!) - Math.floor(t / 1000) * 1000;
  };
  const guess = wallClockAsUtc - offsetAt(wallClockAsUtc);
  return new Date(wallClockAsUtc - offsetAt(guess));
}

/** The pilot's usual question: who opened which patient's record in the last 30 days, counted in the clinic's time zone. */
export const defaultFilters = (now = new Date(), zone = browserZone()): AuditFilters =>
  ({ actor: all, action: patientRecords, from: dateIn(new Date(now.getTime() - 30 * DAY_MS), zone), to: dateIn(now, zone), patientId: null, zone });

/** The day boundaries in the clinic's time zone, as instants. The range is [from 00:00, day after "to" 00:00). */
export const rangeOf = (f: AuditFilters) => ({
  from: f.from ? startOfDayIn(f.from, f.zone).toISOString() : undefined,
  to: f.to ? startOfDayIn(dateIn(new Date(startOfDayIn(f.to, f.zone).getTime() + 1.5 * DAY_MS), f.zone), f.zone).toISOString() : undefined,
});

/** Query string for the list; ids and codes only, no names or search text. */
export function listQuery(f: AuditFilters, after: string | null): string {
  const q = new URLSearchParams();
  if (f.actor !== all) q.set('actorUserId', f.actor);
  if (f.action === patientRecords) q.set('patientRecords', 'true');
  else if (f.action !== all) q.set('action', f.action);
  const { from, to } = rangeOf(f);
  if (from) q.set('from', from);
  if (to) q.set('to', to);
  if (after) q.set('after', after);
  return q.toString();
}

export const exportBody = (f: AuditFilters) => ({
  actorUserId: f.actor === all ? null : f.actor,
  patientId: f.patientId,
  action: f.action === all || f.action === patientRecords ? null : f.action,
  patientRecords: f.action === patientRecords,
  ...rangeOf(f),
});

export const browserZone = () => Intl.DateTimeFormat().resolvedOptions().timeZone;
