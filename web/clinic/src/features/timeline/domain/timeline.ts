import { sparkline, type ChartSource, type ObservationCode, type Trend } from '../../patient360/domain/chart';

// The longitudinal timeline (MDV-P2-005, E-02): what the server answers, and the filters. The server decides every access.
export const timelineKinds = ['Encounter', 'Lab', 'Medicine', 'Document', 'Order'] as const;
export type TimelineKind = (typeof timelineKinds)[number];
export const timelineSources = ['ClinicianVerified', 'PatientReported', 'Imported', 'SyncedFromHis'] as const satisfies readonly ChartSource[];

export type TimelineCode = 'Consultation' | 'ConsultationInProgress' | 'HbA1c' | 'MedicineActive' | 'MedicineStopped'
  | 'DocumentLabReport' | 'DocumentImaging' | 'DocumentPrescription' | 'DocumentReferral' | 'DocumentOther'
  | 'OrderDraft' | 'OrderOrdered' | 'OrderNotIncluded';
export type TimelineEntry = {
  id: string; kind: TimelineKind; at: string;
  /** What it is, translated by the screen; `title` and `detail` are the data that go with it (a medicine's name and dose, a result's value). */
  code: TimelineCode; title: string | null; detail: string | null; author: string | null; source: ChartSource;
  /** The visit a prescribed medicine comes from (MDV-1087). */
  encounterId?: string | null;
};
export type TimelinePage = { entries: TimelineEntry[]; next: string | null; timeZone: string };
export type TrendsAnswer = { trends: Trend[]; timeZone: string };

/** Several kinds may be on at once (none = all); one source or none. */
export type TimelineFilters = { kinds: TimelineKind[]; source: ChartSource | null };
export const noFilters: TimelineFilters = { kinds: [], source: null };

export const hasFilters = (f: TimelineFilters) => f.kinds.length > 0 || f.source !== null;

/** Turns one kind on or off; turning the last one off shows all again. */
export const toggleKind = (f: TimelineFilters, kind: TimelineKind): TimelineFilters =>
  ({ ...f, kinds: f.kinds.includes(kind) ? f.kinds.filter(k => k !== kind) : [...f.kinds, kind] });

/** Query string for the server: codes and a cursor only, never patient data. */
export function timelineQuery(f: TimelineFilters, before: string | null): string {
  const q = new URLSearchParams();
  if (f.kinds.length > 0) q.set('kinds', f.kinds.join(','));
  if (f.source) q.set('source', f.source);
  if (before) q.set('before', before);
  const s = q.toString();
  return s ? `?${s}` : '';
}

/** Reads remembered filters back; anything that is not a valid filter set is dropped, so a stale or edited value cannot break the page. */
export function parseFilters(raw: string | null): TimelineFilters {
  try {
    const v = JSON.parse(raw ?? '') as { kinds?: unknown; source?: unknown };
    const kinds = Array.isArray(v.kinds) ? timelineKinds.filter(k => (v.kinds as unknown[]).includes(k)) : [];
    const source = timelineSources.find(s => s === v.source) ?? null;
    return { kinds, source };
  } catch {
    return noFilters;
  }
}

/** The measures a doctor can switch between. Blood pressure is the systolic and diastolic series together. */
export const trendChoices = ['HbA1c', 'BloodPressure', 'Weight'] as const;
export type TrendChoice = (typeof trendChoices)[number];

export type TrendRow = { takenAt: string; value: string; source: ChartSource };
export type TrendSeries = { unit: string; rows: TrendRow[]; points: number[]; second: number[] };

const fmt = (n: number) => String(Number(n.toFixed(2)));
const byCode = (trends: Trend[], code: ObservationCode) => trends.find(t => t.code === code);

/** The lines the chart draws, on one scale so the diastolic line sits under the systolic one. */
export function chartLines(s: TrendSeries): { main: { x: number; y: number }[]; second: { x: number; y: number }[] } {
  const all = [...s.points, ...s.second];
  const [min, max] = [Math.min(...all), Math.max(...all)];
  return { main: sparkline(s.points, min, max), second: s.second.length > 0 ? sparkline(s.second, min, max) : [] };
}

/** Rows for the table, newest first (the chart runs oldest to newest), and the numbers the chart draws. Null: no readings of this measure. */
export function trendSeries(trends: Trend[], choice: TrendChoice): TrendSeries | null {
  const main = byCode(trends, choice === 'BloodPressure' ? 'BloodPressureSystolic' : choice);
  if (!main || main.points.length === 0) return null;
  const dia = choice === 'BloodPressure' ? byCode(trends, 'BloodPressureDiastolic') : undefined;
  const diaAt = new Map(dia?.points.map(p => [p.takenAt, p.value]));
  const rows = main.points.map(p => ({ takenAt: p.takenAt, source: p.source, value: diaAt.has(p.takenAt) ? `${fmt(p.value)}/${fmt(diaAt.get(p.takenAt)!)}` : fmt(p.value) }));
  return { unit: main.unit, rows: rows.toReversed(), points: main.points.map(p => p.value), second: main.points.flatMap(p => (diaAt.has(p.takenAt) ? [diaAt.get(p.takenAt)!] : [])) };
}
