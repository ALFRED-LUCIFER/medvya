import { z } from 'zod';

// The older adult review (MDV-P2-037): what the server answers. The server validates every score and works out every flag; nothing here decides one.
export type GeriatricAssessment = {
  id: string; assessedAt: string; author: string | null;
  fallsLast12Months: number | null; timedUpAndGoSeconds: number | null; tugAtRisk: boolean | null;
  lyingSystolic: number | null; lyingDiastolic: number | null; standingSystolic: number | null; standingDiastolic: number | null;
  systolicDrop: number | null; diastolicDrop: number | null; orthostatic: boolean | null;
  clinicalFrailtyScale: number | null; miniCog: number | null; adl: number | null; iadl: number | null; phq2: number | null; nutritionNote: string | null;
};
export type GeriatricHistory = { assessments: GeriatricAssessment[]; timeZone: string; rulesSignedOff: boolean };

export type ReviewRule = 'Benzodiazepine' | 'LongTermPpi' | 'SystemicNsaid' | 'RaasLowEgfrHighPotassium';
export type ReviewSuggestion = { medicineId: string; medicine: string; rule: ReviewRule; weeksOnChart: number | null; egfr: number | null; potassiumHundredths: number | null };
export type MedicineReview = {
  suggestions: ReviewSuggestion[]; notChecked: { medicineId: string; medicine: string; reason: 'NotOnFormulary' | 'NoEgfr' | 'NoPotassium' }[];
  medicines: number; olderAdult: boolean; rulesSignedOff: boolean;
};

/** One line of the review card: alert (a flag line passed), ok (measured, not flagged) or plain (a score shown on its scale, not flagged). */
export type GeriKey = 'falls' | 'tug' | 'tugAtRisk' | 'orthostatic' | 'standing' | 'cfs' | 'miniCog' | 'adl' | 'iadl' | 'phq2' | 'nutrition';
export type GeriLine = { key: GeriKey; values: Record<string, string | number>; level: 'alert' | 'ok' | 'plain' };

export function geriLines(a: GeriatricAssessment): GeriLine[] {
  const lines: GeriLine[] = [];
  if (a.fallsLast12Months !== null) lines.push({ key: 'falls', values: { count: a.fallsLast12Months }, level: 'plain' });
  if (a.timedUpAndGoSeconds !== null) lines.push({ key: a.tugAtRisk ? 'tugAtRisk' : 'tug', values: { seconds: a.timedUpAndGoSeconds }, level: a.tugAtRisk ? 'alert' : 'ok' });
  if (a.lyingSystolic !== null && a.systolicDrop !== null && a.diastolicDrop !== null) {
    lines.push({
      key: a.orthostatic ? 'orthostatic' : 'standing',
      values: { lying: `${a.lyingSystolic}/${a.lyingDiastolic}`, standing: `${a.standingSystolic}/${a.standingDiastolic}`, systolic: a.systolicDrop, diastolic: a.diastolicDrop },
      level: a.orthostatic ? 'alert' : 'ok',
    });
  }
  const scores = [['cfs', a.clinicalFrailtyScale, 9], ['miniCog', a.miniCog, 5], ['adl', a.adl, 6], ['iadl', a.iadl, 8], ['phq2', a.phq2, 6]] as const;
  for (const [key, score, max] of scores) if (score !== null) lines.push({ key, values: { score, max }, level: 'plain' });
  if (a.nutritionNote) lines.push({ key: 'nutrition', values: { note: a.nutritionNote }, level: 'plain' });
  return lines;
}

// The form keeps what was typed; blank is "not measured". The server's scales are repeated here only to say so before sending.
const score = (min: number, max: number) => z.string().trim().refine(v => v === '' || (/^\d{1,3}$/.test(v) && +v >= min && +v <= max), 'out_of_scale');
export const geriatricFormSchema = z.object({
  fallsLast12Months: score(0, 99),
  timedUpAndGoSeconds: z.string().trim().refine(v => v === '' || (/^\d{1,3}([.,]\d)?$/.test(v) && +v.replace(',', '.') >= 1 && +v.replace(',', '.') <= 120), 'out_of_scale'),
  lyingSystolic: score(50, 300), lyingDiastolic: score(20, 200), standingSystolic: score(50, 300), standingDiastolic: score(20, 200),
  clinicalFrailtyScale: score(1, 9), miniCog: score(0, 5), adl: score(0, 6), iadl: score(0, 8), phq2: score(0, 6),
  nutritionNote: z.string().trim().max(500, 'too_long'),
}).superRefine((f, ctx) => {
  const bp = [f.lyingSystolic, f.lyingDiastolic, f.standingSystolic, f.standingDiastolic];
  if (bp.some(v => v !== '') && bp.some(v => v === '')) ctx.addIssue({ code: 'custom', path: ['lyingSystolic'], message: 'incomplete' });
  if (Object.values(f).every(v => v === '')) ctx.addIssue({ code: 'custom', path: ['fallsLast12Months'], message: 'empty' });
});
export type GeriatricForm = z.infer<typeof geriatricFormSchema>;
/** The form's error codes (Zod messages), each a key under olderAdult.error. */
export type GeriError = 'out_of_scale' | 'incomplete' | 'empty' | 'too_long';
export const geriatricFields = ['fallsLast12Months', 'timedUpAndGoSeconds', 'lyingSystolic', 'lyingDiastolic', 'standingSystolic', 'standingDiastolic',
  'clinicalFrailtyScale', 'miniCog', 'adl', 'iadl', 'phq2'] as const satisfies readonly (keyof GeriatricForm)[];
export const emptyGeriatricForm: GeriatricForm = {
  fallsLast12Months: '', timedUpAndGoSeconds: '', lyingSystolic: '', lyingDiastolic: '', standingSystolic: '', standingDiastolic: '',
  clinicalFrailtyScale: '', miniCog: '', adl: '', iadl: '', phq2: '', nutritionNote: '',
};

/** The request body: blank is null, Timed Up and Go in seconds (a decimal comma is read as a point). */
export function toRecord(f: GeriatricForm) {
  const n = (v: string) => (v.trim() === '' ? null : Number(v.trim().replace(',', '.')));
  return {
    fallsLast12Months: n(f.fallsLast12Months), timedUpAndGoSeconds: n(f.timedUpAndGoSeconds),
    lyingSystolic: n(f.lyingSystolic), lyingDiastolic: n(f.lyingDiastolic), standingSystolic: n(f.standingSystolic), standingDiastolic: n(f.standingDiastolic),
    clinicalFrailtyScale: n(f.clinicalFrailtyScale), miniCog: n(f.miniCog), adl: n(f.adl), iadl: n(f.iadl), phq2: n(f.phq2),
    nutritionNote: f.nutritionNote.trim() === '' ? null : f.nutritionNote.trim(),
  };
}
