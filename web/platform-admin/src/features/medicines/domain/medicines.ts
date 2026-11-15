import { z } from 'zod';
import type { PlatformRole } from '../../session/domain/session';
import { reasonSchema } from '../../team/domain/team';

// Mirrors SharedMedicineView and FormularyRules (src/Modules/Prescribing): the server is the authority, these only save a round trip.
export type Country = 'IN' | 'AE';
export const countries: readonly Country[] = ['IN', 'AE'];
export type RegulatoryClass = 'None' | 'ScheduleH' | 'ScheduleH1' | 'ScheduleX' | 'Controlled' | 'NotVerified';
export type Source = 'Starter' | 'ClinicAdded' | 'Medvya' | 'Register';

export type Medicine = {
  id: string; country: Country; generic: string; strength: string; form: string; brands: string[]; nationalCode: string | null;
  regClass: RegulatoryClass; atc: string | null; defaultSig: string | null; source: Source; registerCode: string | null; registerRelease: string | null; active: boolean;
};

/** The classes a country uses (FormularyRules.Classes); Not verified until an advisor confirms the class (MDV-P5-064). */
export const classesFor = (country: Country): RegulatoryClass[] =>
  country === 'IN' ? ['None', 'ScheduleH', 'ScheduleH1', 'ScheduleX', 'NotVerified'] : ['None', 'Controlled', 'NotVerified'];

export const forms = ['Tablet', 'Capsule', 'Syrup', 'Suspension', 'Injection', 'Cream', 'Ointment', 'Drops', 'Inhaler', 'Patch', 'Suppository', 'Powder', 'Solution'] as const;

/** The first rows of a search, as the clinic's formulary screen shows them; the field's hint says so, so a long list is narrowed by typing. */
export const searchLimit = 50;

/** Owner and operations change the list; every other role reads it (the server answers 403, the buttons are simply absent). */
export const canWrite = (role: PlatformRole) => role === 'Owner' || role === 'Operations';

/** Column definitions are data: each is its heading's i18n key; the medicine identifies a row (MDV-P2-054). */
export const medicineColumns = ['platform.medicines.col.medicine', 'platform.medicines.col.class', 'platform.medicines.col.source', 'platform.medicines.col.status'] as const;

const optional = (max: number, code = 'tooLong') => z.string().trim().max(max, code);

/** Add and correct share one form; a correction leaves the class out (it has its own action). */
export const medicineForm = z.object({
  generic: z.string().trim().min(1, 'required').max(200, 'tooLong'),
  strength: z.string().trim().min(1, 'required').max(50, 'tooLong'),
  form: z.enum(forms, 'required'),
  brands: optional(1100),
  regClass: z.enum(['None', 'ScheduleH', 'ScheduleH1', 'ScheduleX', 'Controlled', 'NotVerified'], 'required'),
  nationalCode: optional(30),
  atc: optional(7),
  defaultSig: optional(200),
  reason: reasonSchema,
});
export type MedicineForm = z.infer<typeof medicineForm>;

export const emptyForm = (): MedicineForm => ({ generic: '', strength: '', form: 'Tablet', brands: '', regClass: 'NotVerified', nationalCode: '', atc: '', defaultSig: '', reason: '' });

// A form outside the list (a future register row) is kept as is, so the form check asks for a choice instead of silently picking one.
export const formOf = (m: Medicine): MedicineForm => ({
  generic: m.generic, strength: m.strength, form: m.form as MedicineForm['form'],
  brands: m.brands.join(', '), regClass: m.regClass, nationalCode: m.nationalCode ?? '', atc: m.atc ?? '', defaultSig: m.defaultSig ?? '', reason: '',
});

/** A blank stays blank: nothing is sent for an empty optional field. */
export const toBody = (f: MedicineForm) => ({
  generic: f.generic, strength: f.strength, form: f.form, regClass: f.regClass, reason: f.reason,
  brands: f.brands.split(',').map(b => b.trim()).filter(Boolean),
  nationalCode: f.nationalCode || null, atc: f.atc || null, defaultSig: f.defaultSig || null,
});

/** The words for a server code under a field (400) or of a refusal (409, 404); an unknown code reads as "failed". */
export const errorKeyOf = (code: string) =>
  code === 'required' ? 'platform.error.required'
  : code === 'too_long' || code === 'tooLong' ? 'platform.error.tooLong'
  : code === 'reason.short' || code === 'reason.long' ? `platform.error.${code}`
  : code === 'too_many' || code === 'unknown' || code === 'invalid' ? `formulary.error.${code}`
  : code === 'not_in_country' || code === 'already_listed' || code === 'gone' ? `platform.medicines.error.${code}`
  : 'platform.error.failed';

/** What the reason form is for: setting the class or deactivating one medicine. */
export type PendingAction = { kind: 'class' | 'deactivate'; medicine: Medicine };
/** The open add or correction form. */
export type Editing = { kind: 'add' } | { kind: 'correct'; medicine: Medicine };
