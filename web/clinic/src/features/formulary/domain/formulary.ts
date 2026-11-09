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

const optional = (max: number) => z.string().trim().max(max);

export const medicineSchema = z.object({
  generic: z.string().trim().min(1).max(200),
  strength: z.string().trim().min(1).max(50),
  form: z.enum(forms),
  brands: optional(1100),
  regClass: z.enum(['None', 'ScheduleH', 'ScheduleH1', 'ScheduleX', 'Controlled']),
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

/** What a refusal is worded as: the server's field codes, plus "conflict" (already listed) and "failed" (anything else). */
export type ProblemCode = 'required' | 'too_long' | 'too_many' | 'unknown' | 'invalid' | 'not_in_country' | 'conflict' | 'failed';
const problemCodes: readonly string[] = ['required', 'too_long', 'too_many', 'unknown', 'invalid', 'not_in_country'];

/** The field and code under a field for a server answer ("regClass:not_in_country"; a 409 has the code alone). Unknown codes read as "failed". */
export function splitProblem(code: string | undefined): { field: string | null; code: ProblemCode } {
  if (code === 'already_listed') return { field: null, code: 'conflict' };
  const [field, reason] = code?.split(':') ?? [];
  return reason && problemCodes.includes(reason) ? { field: field ?? null, code: reason as ProblemCode } : { field: null, code: 'failed' };
}
