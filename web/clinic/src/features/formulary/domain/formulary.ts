import { z } from 'zod';

// Mirrors src/Modules/Prescribing/Medvya.Prescribing.Application/IFormulary.cs (the server is the authority; these checks only save a round trip).
export type RegulatoryClass = 'None' | 'ScheduleH' | 'ScheduleH1' | 'ScheduleX' | 'Controlled';
export type FormularyItem = {
  id: string;
  generic: string;
  strength: string;
  form: Form;
  brands: string[];
  nationalCode: string | null;
  regClass: RegulatoryClass;
  /** Codes the screen says in words (and the prescription print follows): the warning text and who keeps a copy. */
  printRequirements: PrintRequirement[];
  atc: string | null;
  defaultSig: string | null;
  source: 'Starter' | 'ClinicAdded';
  active: boolean;
};
export type PrintRequirement = 'warning_schedule_h' | 'warning_schedule_h1' | 'warning_schedule_x' | 'warning_controlled' | 'pharmacist_keeps_copy' | 'duplicate_prescription';
export type OrderSet = { id: string; name: string; items: string[] };
export type Country = 'IN' | 'AE' | null;

export const forms = ['Tablet', 'Capsule', 'Syrup', 'Suspension', 'Injection', 'Cream', 'Ointment', 'Drops', 'Inhaler', 'Patch', 'Suppository', 'Powder', 'Solution'] as const;
export type Form = (typeof forms)[number];

/** The classes a country uses (the server refuses any other). Without a known country only the ordinary class is offered. */
export const classesFor = (country: Country): RegulatoryClass[] =>
  country === 'IN' ? ['None', 'ScheduleH', 'ScheduleH1', 'ScheduleX'] : country === 'AE' ? ['None', 'Controlled'] : ['None'];

/** Column definitions are data; the label for each is the i18n key `formulary.col.<key>`. */
export const formularyColumns = ['generic', 'strength', 'form', 'brands', 'class', 'code', 'source'] as const;
export type FormularyColumn = (typeof formularyColumns)[number];

/** The search answers with at most this many medicines (MaxLimit in FormularyService); a full answer says so and asks to narrow. */
export const FORMULARY_LIMIT = 50;

const regClasses = ['None', 'ScheduleH', 'ScheduleH1', 'ScheduleX', 'Controlled'] as const;
/** The columns people sort by: the name, the class from ordinary to strictest, and whether the clinic added it. */
export const formularySort = {
  generic: (i: FormularyItem) => i.generic,
  class: (i: FormularyItem) => regClasses.indexOf(i.regClass),
  source: (i: FormularyItem) => i.source,
};
export type FormularySortColumn = keyof typeof formularySort;
export const isFormularySortable = (c: FormularyColumn): c is FormularySortColumn => c in formularySort;

const optional = (max: number) => z.string().trim().max(max);

export const medicineSchema = z.object({
  generic: z.string().trim().min(1).max(200),
  strength: z.string().trim().min(1).max(50),
  form: z.enum(forms),
  brands: optional(1100),
  regClass: z.enum(regClasses),
  nationalCode: optional(30),
  atc: optional(7),
  defaultSig: optional(200),
});
export type MedicineForm = z.infer<typeof medicineSchema>;
export const emptyMedicine = (): Partial<MedicineForm> => ({ generic: '', strength: '', brands: '', nationalCode: '', atc: '', defaultSig: '' });

/** A blank stays blank: nothing is sent for an empty optional field. */
export const toMedicineBody = (f: MedicineForm) => ({
  generic: f.generic, strength: f.strength, form: f.form, regClass: f.regClass,
  brands: f.brands.split(',').map(b => b.trim()).filter(Boolean),
  nationalCode: f.nationalCode || null, atc: f.atc || null, defaultSig: f.defaultSig || null,
});

export const orderSetSchema = z.object({ name: z.string().trim().min(1).max(100), items: z.string().trim().min(1) });
export type OrderSetForm = z.infer<typeof orderSetSchema>;
export const toOrderSetBody = (f: OrderSetForm) => ({ name: f.name, items: f.items.split('\n').map(i => i.trim()).filter(Boolean) });

// Brand names in bulk (MDV-P1-088). The server is the authority on every row; this only splits the pasted text and words its answer.
/** The allergy classes of the safety rules (SafetyRules.Classes on the server), as a row names them; "none" for a medicine in no class. */
export const allergyClasses = ['penicillins', 'cephalosporins', 'sulfonamides', 'fluoroquinolones', 'macrolides', 'nsaids', 'opioids', 'none'] as const;
export const brandRowsSchema = z.object({ rows: z.string() });
export type BrandRowsForm = z.infer<typeof brandRowsSchema>;
export type BrandRow = { brand: string; generic: string; strength: string; form: string; allergyClass: string };
export type BrandProblemCode = 'none' | 'columns' | 'required' | 'too_long' | 'too_many' | 'not_listed' | 'not_checked' | 'mismatch' | 'already_listed' | 'is_generic' | 'failed';
/** A refused row, counted from 1 as the admin sees it; null when the problem is the whole request. */
export type BrandProblem = { row: number | null; code: BrandProblemCode };
const brandRowCodes: readonly string[] = ['required', 'too_long', 'not_listed', 'not_checked', 'mismatch', 'already_listed', 'is_generic'];

/** One brand per line: brand, generic, strength, form, allergy class. Tabs (a paste from a spreadsheet) or commas; blank lines are skipped. */
export function parseBrandRows(text: string): { rows: BrandRow[]; problems: BrandProblem[] } {
  const lines = text.split('\n').map(l => l.trim()).filter(Boolean);
  const cells = lines.map(l => l.split(l.includes('\t') ? '\t' : ',').map(c => c.trim()));
  const problems: BrandProblem[] = lines.length === 0 ? [{ row: null, code: 'none' }] : cells.flatMap((c, i) => (c.length === 5 ? [] : [{ row: i + 1, code: 'columns' as const }]));
  return { rows: cells.map(([brand = '', generic = '', strength = '', form = '', allergyClass = '']) => ({ brand, generic, strength, form, allergyClass })), problems };
}

/** The server's refusal ("rows[3].generic": ["not_listed"]) as one problem a row and code. Anything else reads as "failed". */
export function brandProblems(body: unknown): BrandProblem[] {
  const errors = Object.entries((body as { errors?: Record<string, string[]> } | null)?.errors ?? {});
  const found = errors.flatMap(([field, codes]): BrandProblem[] => {
    const row = /^rows\[(\d+)\]/.exec(field);
    const code = codes[0];
    if (row && code && brandRowCodes.includes(code)) return [{ row: Number(row[1]) + 1, code: code as BrandProblemCode }];
    return field === 'rows' && (code === 'required' || code === 'too_many') ? [{ row: null, code: code === 'required' ? 'none' : 'too_many' }] : [];
  });
  const once = found.filter((p, i) => found.findIndex(o => o.row === p.row && o.code === p.code) === i);
  return once.length > 0 ? once.sort((a, b) => (a.row ?? 0) - (b.row ?? 0)) : [{ row: null, code: 'failed' }];
}

/** What a refusal is worded as: the server's field codes, plus "conflict" (already listed) and "failed" (anything else). */
export type ProblemCode = 'required' | 'too_long' | 'too_many' | 'unknown' | 'invalid' | 'not_in_country' | 'conflict' | 'failed';
const problemCodes: readonly string[] = ['required', 'too_long', 'too_many', 'unknown', 'invalid', 'not_in_country'];

/** The field and code under a field for a server answer ("regClass:not_in_country"; a 409 has the code alone). Unknown codes read as "failed". */
export function splitProblem(code: string | undefined): { field: string | null; code: ProblemCode } {
  if (code === 'already_listed') return { field: null, code: 'conflict' };
  const [field, reason] = code?.split(':') ?? [];
  return reason && problemCodes.includes(reason) ? { field: field ?? null, code: reason as ProblemCode } : { field: null, code: 'failed' };
}
