import type { Role } from '../../session/domain/session';
// Mirrors src/Host.Api/AuditLog/AccessLogEndpoints.cs (the server is the authority). Rows hold ids and codes; the patient's name arrives only
// on the per-patient request, so a list without a chosen patient shows the MRN alone.

export type AuditOutcome = 'Allowed' | 'Denied' | 'NotFound' | 'Rejected';

export type AuditRow = {
  id: string;
  occurredAt: string;
  actorUserId: string | null;
  actorName: string | null;
  role: Role | null;
  action: string;
  resourceType: string;
  patientId: string | null;
  patientMrn: string | null;
  patientName: string | null;
  purpose: string;
  outcome: AuditOutcome;
};

export type AuditPage = { items: AuditRow[]; next: string | null; actions: string[] };

/** Dates are 'YYYY-MM-DD' from the date inputs; 'all' is the "everyone / any action" choice of a select. */
export type AuditFilters = { actor: string; action: string; from: string; to: string; patientId: string | null };

export const all = 'all';

const DAY_MS = 86_400_000;
const isoDate = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

/** The pilot's usual question: the last 30 days. */
export const defaultFilters = (now = new Date()): AuditFilters => ({ actor: all, action: all, from: isoDate(new Date(now.getTime() - 30 * DAY_MS)), to: isoDate(now), patientId: null });

/** The day boundaries in the browser's time zone, as instants. The range is [from 00:00, day after "to" 00:00). */
export const rangeOf = (f: AuditFilters) => ({
  from: f.from ? new Date(`${f.from}T00:00:00`).toISOString() : undefined,
  to: f.to ? new Date(new Date(`${f.to}T00:00:00`).getTime() + DAY_MS).toISOString() : undefined,
});

/** Query string for the list; ids and codes only, no names or search text. */
export function listQuery(f: AuditFilters, after: string | null): string {
  const q = new URLSearchParams();
  if (f.actor !== all) q.set('actorUserId', f.actor);
  if (f.action !== all) q.set('action', f.action);
  const { from, to } = rangeOf(f);
  if (from) q.set('from', from);
  if (to) q.set('to', to);
  if (after) q.set('after', after);
  return q.toString();
}

export const exportBody = (f: AuditFilters) => ({
  actorUserId: f.actor === all ? null : f.actor,
  patientId: f.patientId,
  action: f.action === all ? null : f.action,
  ...rangeOf(f),
});

export const browserZone = () => Intl.DateTimeFormat().resolvedOptions().timeZone;
