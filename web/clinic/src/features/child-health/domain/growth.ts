import { z } from 'zod';
import { pathOf } from '../../patient360/domain/chart';

// Child growth (MDV-P2-035, E-01). The server works out every z-score, centile and flag from WHO's tables and validates every record; this
// file names what it answers, draws the chart from its numbers and shapes the form.
export type GrowthIndicator = 'WeightForAge' | 'LengthForAge' | 'BmiForAge';
export type BodyPosition = 'Lying' | 'Standing';
export type GrowthAlert = 'BelowThird' | 'CrossedDown' | 'CrossedUp';
export type NoCentiles = 'over_five' | 'sex_other';
export const indicators: GrowthIndicator[] = ['WeightForAge', 'LengthForAge', 'BmiForAge'];
export const positions: BodyPosition[] = ['Lying', 'Standing'];

export type Measurement = { takenOn: string; ageDays: number; weightKg: number | null; lengthCm: number | null; position: BodyPosition | null; headCm: number | null };
export type GrowthPoint = { takenOn: string; ageDays: number; value: number; z: number; centile: number };
export type CentileLine = { centile: 3 | 15 | 50 | 85 | 97; points: { ageDays: number; value: number }[] };
export type GrowthSeries = { indicator: GrowthIndicator; points: GrowthPoint[]; lines: CentileLine[]; flags: GrowthAlert[] };
export type Growth = {
  child: boolean; signedOff: boolean; noCentiles: NoCentiles | null; measurements: Measurement[]; series: GrowthSeries[];
  velocity: { changeKg: number; days: number; from: string; to: string } | null;
};

/** The chart's box (the design's 520 × 260) and its plot area. */
export const growthBox = { width: 520, height: 260, left: 44, right: 488, top: 20, bottom: 232 } as const;
const yearDays = 365.25; // the lines end at 60 months, day 1826: a day short of 5 × 365.25

/** The centile lines, the child's points and the axis ticks in the box. Null when the series has no lines. */
export function growthChart(s: GrowthSeries) {
  const all = [...s.lines.flatMap(l => l.points), ...s.points];
  if (s.lines.length === 0 || all.length === 0) return null;
  const maxDay = Math.max(...all.map(p => p.ageDays));
  const [lo, hi] = [Math.min(...all.map(p => p.value)), Math.max(...all.map(p => p.value))];
  const pad = (hi - lo) * 0.05;
  const [min, max] = [lo - pad, hi + pad];
  const b = growthBox;
  const at = (day: number, value: number) => ({
    x: b.left + (day / maxDay) * (b.right - b.left),
    y: b.bottom - ((value - min) / (max - min)) * (b.bottom - b.top),
  });
  const line = (l: CentileLine) => l.points.map(p => at(p.ageDays, p.value));
  const step = niceStep((max - min) / 5);
  const yTicks: { y: number; value: number }[] = [];
  for (let v = Math.ceil(min / step) * step; v <= max; v += step) yTicks.push({ y: at(0, v).y, value: Number(v.toFixed(2)) });
  const byCentile = (c: number) => s.lines.find(l => l.centile === c);
  const outer = [byCentile(3), byCentile(97)], inner = [byCentile(15), byCentile(85)];
  const band = ([low, high]: (CentileLine | undefined)[]) => (low && high ? `${pathOf(line(high))} ${pathOf([...line(low)].reverse()).replace(/^M/, 'L')} Z` : null);
  return {
    lines: s.lines.map(l => ({ centile: l.centile, d: pathOf(line(l)), end: line(l).at(-1)! })),
    bands: [band(outer), band(inner)].filter((d): d is string => d !== null),
    child: s.points.map(p => at(p.ageDays, p.value)),
    childPath: pathOf(s.points.map(p => at(p.ageDays, p.value))),
    xTicks: Array.from({ length: Math.floor((maxDay + 1) / yearDays) + 1 }, (_, years) => ({ x: at(years * yearDays, min).x, years })),
    yTicks,
  };
}

function niceStep(raw: number) {
  const unit = 10 ** Math.floor(Math.log10(raw));
  return [1, 2, 5, 10].map(m => m * unit).find(s => s >= raw) ?? 10 * unit;
}

/** The latest centile and the trend against the point before, for the chart's text alternative. Null: no points. */
export function latestOf(s: GrowthSeries) {
  const latest = s.points.at(-1);
  if (!latest) return null;
  const before = s.points.at(-2) ?? null;
  const trend = !before ? 'single' : latest.z < before.z ? 'down' : latest.z > before.z ? 'up' : 'same';
  return { latest, before, trend } as const;
}

/** A centile as a whole number for words; never 0 or 100, which no measurement is. */
export const centileWord = (c: number) => Math.min(99, Math.max(1, Math.round(c)));

// A length needs its position, and at least one measurement is sent: said here first, refused by the server too.
const num = z.string().trim().refine(v => v === '' || Number.isFinite(Number(v.replace(',', '.'))), 'number');
export const growthSchema = z.object({
  takenOn: z.string().min(1),
  weightKg: num, lengthCm: num, position: z.enum(['', 'Lying', 'Standing']), headCm: num,
})
  .refine(d => d.weightKg !== '' || d.lengthCm !== '' || d.headCm !== '', { path: ['weightKg'], message: 'required' })
  .refine(d => d.lengthCm === '' || d.position !== '', { path: ['position'], message: 'required' });
export type GrowthDraft = z.infer<typeof growthSchema>;
export const emptyGrowth: GrowthDraft = { takenOn: '', weightKg: '', lengthCm: '', position: '', headCm: '' };

/** The body the server takes. Blank stays null, never guessed. */
export function growthBody(d: GrowthDraft) {
  const n = (s: string) => (s.trim() === '' ? null : Number(s.trim().replace(',', '.')));
  return { takenOn: d.takenOn, weightKg: n(d.weightKg), lengthCm: n(d.lengthCm), position: d.lengthCm.trim() === '' || d.position === '' ? null : d.position, headCm: n(d.headCm) };
}

export type GrowthProblem = 'not_a_child' | 'invalid' | 'refused' | 'failed';
export function growthProblemOf(status: number | undefined, code: string | undefined): GrowthProblem {
  if (code === 'not_a_child') return code;
  if (status === 400) return 'invalid';
  if (status === 403) return 'refused';
  return 'failed';
}
