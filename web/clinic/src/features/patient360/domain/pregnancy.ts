import { z } from 'zod';
import type { OpenPregnancy } from './chart';

// The pregnancy card (MDV-P2-034). The server works out the due date, gestation and contacts and decides who may write; this file only
// shapes what the card says and the forms it sends.
export const termDays = 280;
export const outcomes = ['LiveBirth', 'Stillbirth', 'Miscarriage', 'Termination', 'Ectopic', 'EnteredInError'] as const;

export type Trimester = 1 | 2 | 3;
export type PregnancyForm = 'start' | 'close' | 'readings';
export const pregnancyConflicts = ['already_open', 'no_open_pregnancy', 'pregnancy_open', 'no_open_visit'] as const;
/** What the card says when a write is refused: the 409 codes in words, or refused (403), invalid (400) or failed. */
export type PregnancyProblem = 'refused' | 'invalid' | 'failed' | (typeof pregnancyConflicts)[number];

/** Weeks and days of gestation, the trimester (first to 13 w 6 d, second to 27 w 6 d) and how far along 40 weeks, in percent (capped at 100). */
export function gestation(p: Pick<OpenPregnancy, 'gestationDays'>): { weeks: number; days: number; trimester: Trimester; percent: number } {
  const g = Math.max(0, p.gestationDays);
  return { weeks: Math.floor(g / 7), days: g % 7, trimester: g < 98 ? 1 : g < 196 ? 2 : 3, percent: Math.min(100, Math.round((g / termDays) * 1000) / 10) };
}

/** "G2 P1 A0 L1"; an unknown count shows as "–", never as 0. */
export const gpal = (p: Pick<OpenPregnancy, 'gravida' | 'para' | 'abortions' | 'living'>) =>
  `G${p.gravida ?? '–'} P${p.para ?? '–'} A${p.abortions ?? '–'} L${p.living ?? '–'}`;

const count = (min: number) => z.union([z.literal(''), z.coerce.number().int().min(min).max(30)]).optional();
const optionalDate = z.union([z.literal(''), z.iso.date()]).optional();

export const startSchema = z.object({
  lmp: optionalDate, scanDate: optionalDate,
  scanWeeks: z.union([z.literal(''), z.coerce.number().int().min(0).max(44)]).optional(),
  scanDays: z.union([z.literal(''), z.coerce.number().int().min(0).max(6)]).optional(),
  gravida: count(1), para: count(0), abortions: count(0), living: count(0),
}).refine(v => !!v.lmp || !!v.scanDate, { path: ['lmp'], message: 'required' })
  .refine(v => !v.scanDate || (v.scanWeeks !== undefined && v.scanWeeks !== ''), { path: ['scanWeeks'], message: 'required' });
export type StartForm = z.input<typeof startSchema>;

export const closeSchema = z.object({ outcome: z.enum(outcomes) });
export type CloseForm = z.infer<typeof closeSchema>;

const reading = (min: number, max: number) => z.union([z.literal(''), z.coerce.number().min(min).max(max)]).optional();
export const readingsSchema = z.object({ fundalHeightCm: reading(5, 50), fetalHeartRate: reading(50, 250) })
  .refine(v => (v.fundalHeightCm ?? '') !== '' || (v.fetalHeartRate ?? '') !== '', { path: ['fundalHeightCm'], message: 'required' });
export type ReadingsForm = z.input<typeof readingsSchema>;

const orNull = <T,>(v: T | '' | undefined) => (v === '' || v === undefined ? null : v);

/** The body the server takes: a blank is null (unknown), never 0. */
export function startBody(form: StartForm) {
  const v = startSchema.parse(form);
  return {
    lmp: orNull(v.lmp), scanDate: orNull(v.scanDate), scanWeeks: orNull(v.scanWeeks), scanDays: orNull(v.scanDays),
    gravida: orNull(v.gravida), para: orNull(v.para), abortions: orNull(v.abortions), living: orNull(v.living),
  };
}

export function readingsBody(form: ReadingsForm) {
  const v = readingsSchema.parse(form);
  return { fundalHeightCm: orNull(v.fundalHeightCm), fetalHeartRate: orNull(v.fetalHeartRate) };
}
