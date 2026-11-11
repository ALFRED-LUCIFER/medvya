import { z } from 'zod';
import type { SexAtBirth } from '../../patients/domain/directory';
import type { ChartSource } from '../../patient360/domain/chart';

// Nurse intake (MDV-P2-007). The server decides what is possible and what is flagged (VitalRules); this file only shapes the form and reads answers.
export const vitalKinds = ['SystolicBp', 'DiastolicBp', 'Pulse', 'Temperature', 'OxygenSaturation', 'RespiratoryRate', 'Weight', 'Height'] as const;
export type VitalKind = (typeof vitalKinds)[number];
export type VitalAlert = 'High' | 'Low' | 'Changed';

export const vitalUnits: Record<VitalKind, string> = { SystolicBp: 'mmHg', DiastolicBp: 'mmHg', Pulse: 'bpm', Temperature: '°C', OxygenSaturation: '%', Weight: 'kg', Height: 'cm', RespiratoryRate: '/min' };

export type VitalView = {
  kind: VitalKind; value: number; flag: VitalAlert | null; previousValue: number | null; previousAt: string | null; flagKept: boolean; recordedAt: string;
};

/** How sure the clinic is of an allergy (MDV-P2-008); null is "not set yet" and blocks the handoff. */
export const allergyStatuses = ['Confirmed', 'PatientReported', 'Unknown', 'NotAnAllergy'] as const;
export type AllergyStatus = (typeof allergyStatuses)[number];
/** A nurse adds a new allergy as one of these; "Not an allergy" is for one someone else recorded. */
export const newAllergyStatuses = ['Confirmed', 'PatientReported', 'Unknown'] as const satisfies readonly AllergyStatus[];

export type AllergyView = { id: string; substance: string; reaction: string | null; status: AllergyStatus | null; source: ChartSource; statusSetAt: string | null };
export type MedicineView = { id: string; name: string; dose: string | null; frequency: string | null; needsClarification: boolean; source: ChartSource; recordedAt: string };

export type IntakeView = {
  appointmentId: string; patientId: string; facilityId: string; state: string; chiefComplaint: string | null;
  vitals: VitalView[]; allergies: AllergyView[]; medicines: MedicineView[]; noKnownAllergiesConfirmed: boolean;
  escalated: boolean; handedOff: boolean; blockers: string[];
};

export type IntakeScreen = {
  intake: IntakeView;
  patient: { id: string; fullName: string; mrn: string; dateOfBirth: string; ageYears: number; sexAtBirth: SexAtBirth };
  timeZone: string;
};

/** A value is a decimal with at most one place, or blank (not taken: unknown, never zero). */
const reading = z.string().trim().refine(v => v === '' || /^\d{1,3}([.,]\d)?$/.test(v), 'number');
export const intakeFormSchema = z.object({
  chiefComplaint: z.string().max(500),
  SystolicBp: reading, DiastolicBp: reading, Pulse: reading, Temperature: reading, OxygenSaturation: reading, Weight: reading, Height: reading, RespiratoryRate: reading,
});
export type IntakeForm = z.infer<typeof intakeFormSchema>;

export const allergySchema = z.object({
  substance: z.string().trim().min(1).max(100), reaction: z.string().trim().max(500), status: z.enum(newAllergyStatuses),
});
export type AllergyForm = z.infer<typeof allergySchema>;
export const emptyAllergy: AllergyForm = { substance: '', reaction: '', status: 'PatientReported' };

export const medicineSchema = z.object({
  name: z.string().trim().min(1).max(100), dose: z.string().trim().max(50), frequency: z.string().trim().max(50), needsClarification: z.boolean(),
});
export type MedicineForm = z.infer<typeof medicineSchema>;
export const emptyMedicine: MedicineForm = { name: '', dose: '', frequency: '', needsClarification: false };

/** Blockers the screen says as the allergy gate, as opposed to the vitals one. */
export const allergyBlockers = ['allergy_status_required', 'allergies_not_confirmed'] as const;

export const escalateSchema = z.object({ reason: z.string().trim().min(1).max(500) });

/** The form for a saved intake: what the nurse already entered, blank where nothing was taken. */
export function formOf(view: IntakeView | null): IntakeForm {
  const at = (k: VitalKind) => view?.vitals.find(v => v.kind === k)?.value;
  return {
    chiefComplaint: view?.chiefComplaint ?? '',
    ...(Object.fromEntries(vitalKinds.map(k => [k, at(k) === undefined ? '' : String(at(k))])) as Record<VitalKind, string>),
  };
}

/** The save body: blanks are left out. */
export function toRecord(form: IntakeForm) {
  return {
    chiefComplaint: form.chiefComplaint.trim(),
    vitals: vitalKinds.filter(k => form[k].trim() !== '').map(kind => ({ kind, value: Number(form[kind].trim().replace(',', '.')) })),
  };
}

/** A reading that still needs the nurse's decision: flagged, and not kept on purpose. */
export const undecided = (v: VitalView) => v.flag !== null && !v.flagKept;

/** The kilograms moved since the last weight, signed ("+2.2"), when the flag is a weight change. */
export function weightChange(v: VitalView): string | null {
  if (v.flag !== 'Changed' || v.previousValue === null) return null;
  const d = Math.round((v.value - v.previousValue) * 10) / 10;
  return `${d > 0 ? '+' : ''}${d}`;
}

/** Server answers the screen says in words: a field the server refused, or a conflict. */
export type IntakeProblem = { kind: 'invalid'; field: string; code: string } | { kind: 'conflict'; code: string } | { kind: 'refused' } | { kind: 'failed' };

export function intakeProblem(status: number | undefined, code: string | undefined): IntakeProblem {
  if (status === 403) return { kind: 'refused' };
  if (status === 400 && code?.includes(':')) {
    const [field = '', c = ''] = code.split(':');
    return { kind: 'invalid', field, code: c };
  }
  if (status === 409 && code) return { kind: 'conflict', code };
  return { kind: 'failed' };
}

/** "vitals[0].value" and friends are the server's field paths; the form names the kind through the index of the readings sent. */
export function invalidKind(field: string, sent: readonly { kind: VitalKind }[]): VitalKind | null {
  const m = /^vitals\[(\d+)\]/.exec(field);
  return m ? (sent[Number(m[1])]?.kind ?? null) : null;
}
