import { z } from 'zod';

// Mirrors src/Modules/Prescribing/Medvya.Prescribing.Application/IPrescriptions.cs and the dose builder's codes in Domain/Prescription.cs (Sig).
// The server is the authority and writes the readable instruction; these checks only save a round trip.
export const doseUnits = ['tablet', 'capsule', 'ml', 'mg', 'drop', 'puff', 'unit', 'sachet', 'application'] as const;
export const routes = ['oral', 'sublingual', 'topical', 'inhaled', 'eye', 'ear', 'nasal', 'rectal', 'subcutaneous', 'intramuscular', 'intravenous'] as const;
export const frequencies = ['OD', 'MORNING', 'NIGHT', 'BD', 'TDS', 'QID', 'SOS', 'STAT', 'WEEKLY'] as const;
export const timings = ['before_food', 'after_food', 'with_food', 'empty_stomach', 'morning', 'with_breakfast', 'with_lunch', 'with_dinner', 'bedtime'] as const;
export const printLanguages = ['en', 'hi', 'ta', 'ar'] as const;
export type PrintLanguage = (typeof printLanguages)[number];

export type RxLine = {
  id: string | null; kind: 'Prescribe' | 'Discontinue'; source: 'AddedByYou' | 'AddedByColleague' | 'ChartReconciliation';
  formularyItemId: string | null; medicine: string; strength: string | null; form: string | null; freeText: boolean; included: boolean;
  dose: string | null; doseUnit: string | null; route: string | null; frequency: string | null; timing: string | null;
  durationDays: number | null; quantity: number | null; instructions: string | null; reason: string | null; sig: Record<string, string>;
};
export type Medicine = { id: string; generic: string; strength: string; form: string; brands: string[] };

/** The dose builder as the screen edits it: every field text, blank meaning "not given". */
export const sigFields = ['dose', 'doseUnit', 'route', 'frequency', 'timing', 'durationDays', 'quantity', 'instructions'] as const;
export type SigField = (typeof sigFields)[number];
export type SigDraft = Record<SigField, string>;

const optional = <T extends readonly [string, ...string[]]>(codes: T) => z.union([z.literal(''), z.enum(codes)]);
const count = (max: number) => z.string().trim().regex(/^$|^[0-9]{1,5}$/).refine(v => v === '' || (Number(v) >= 1 && Number(v) <= max));
export const sigSchema = z.object({
  dose: z.string().trim().regex(/^$|^[0-9]{1,5}(\.[0-9]{1,3})?$/).refine(v => v === '' || /[1-9]/.test(v)),
  doseUnit: optional(doseUnits), route: optional(routes), frequency: optional(frequencies), timing: optional(timings),
  durationDays: count(365), quantity: count(10_000), instructions: z.string().trim().max(200),
});

export const toDraft = (l: RxLine): SigDraft => ({
  dose: l.dose ?? '', doseUnit: l.doseUnit ?? '', route: l.route ?? '', frequency: l.frequency ?? '', timing: l.timing ?? '',
  durationDays: l.durationDays?.toString() ?? '', quantity: l.quantity?.toString() ?? '', instructions: l.instructions ?? '',
});

/** What the autosave sends: blanks as null, counts as numbers. Only a draft that passed {@link sigSchema}. */
export function toSigBody(d: SigDraft) {
  const text = (v: string) => v.trim() || null;
  const int = (v: string) => (v.trim() === '' ? null : Number(v));
  return {
    dose: text(d.dose), doseUnit: text(d.doseUnit), route: text(d.route), frequency: text(d.frequency), timing: text(d.timing),
    durationDays: int(d.durationDays), quantity: int(d.quantity), instructions: text(d.instructions),
  };
}

/** The fields a draft would be refused for, so the screen can name them before it autosaves. */
export function invalidFields(d: SigDraft): SigField[] {
  const r = sigSchema.safeParse(d);
  return r.success ? [] : [...new Set(r.error.issues.map(i => i.path[0] as SigField))];
}

export const freeTextSchema = z.object({ medicine: z.string().trim().min(1).max(200) });

/** Arabic is printed right to left. */
export const directionOf = (l: PrintLanguage) => (l === 'ar' ? 'rtl' : 'ltr');

export type RxProblem = 'required' | 'too_long' | 'unknown' | 'encounter_ended' | 'failed';
/** One line's autosave: waiting or sent, done, held back because a field is invalid, or refused. */
export type SaveState = 'saving' | 'saved' | 'invalid' | RxProblem;
const problems: readonly string[] = ['required', 'too_long', 'unknown', 'encounter_ended'];
/** The server's answer as a problem: a 409 carries a code; a 400 carries "field:code". Anything else reads as "failed". */
export function problemOf(code: string | undefined): RxProblem {
  const reason = code?.includes(':') ? code.split(':')[1] : code;
  return reason && problems.includes(reason) ? (reason as RxProblem) : 'failed';
}
