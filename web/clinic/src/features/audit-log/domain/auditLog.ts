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
/** Events about a patient's record (MDV audit UX-13), routine list reads included. */
export const patientRecords = 'patientRecords';
/** The action filter's default (MDV-P2-050): a chart opened or changed, without the routine reads a list writes per patient. A view only. */
export const chart = 'chart';
/** The filter's views, as opposed to one action code. */
const views = [all, patientRecords, chart];

/**
 * The words for an action code (MDV-P1-052, MDV-P2-050), or null for a code the catalog does not know: the screen then says "Other recorded
 * activity", never the code. auditLog.test.ts fails on a server code with no name, so null is a backstop. Codes stay codes in the CSV export.
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
  ({ actor: all, action: chart, from: dateIn(new Date(now.getTime() - 30 * DAY_MS), zone), to: dateIn(now, zone), patientId: null, zone });

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
  else if (f.action === chart) q.set('chart', 'true');
  else if (f.action !== all) q.set('action', f.action);
  const { from, to } = rangeOf(f);
  if (from) q.set('from', from);
  if (to) q.set('to', to);
  if (after) q.set('after', after);
  return q.toString();
}

/** The export holds every row whatever the view (MDV-P2-050 AC4); a chosen staff member, patient, action code and dates still narrow it. */
export const exportBody = (f: AuditFilters) => ({
  actorUserId: f.actor === all ? null : f.actor,
  patientId: f.patientId,
  action: views.includes(f.action) ? null : f.action,
  patientRecords: false,
  ...rangeOf(f),
});

export const browserZone = () => Intl.DateTimeFormat().resolvedOptions().timeZone;
