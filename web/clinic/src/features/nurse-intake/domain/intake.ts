import { z } from 'zod';
import type { SexAtBirth } from '../../patients/domain/directory';

// Nurse intake (MDV-P2-007). The server decides what is possible and what is flagged (VitalRules); this file only shapes the form and reads answers.
export const vitalKinds = ['SystolicBp', 'DiastolicBp', 'Pulse', 'Temperature', 'OxygenSaturation', 'Weight'] as const;
export type VitalKind = (typeof vitalKinds)[number];
export type VitalAlert = 'High' | 'Low' | 'Changed';

export const vitalUnits: Record<VitalKind, string> = { SystolicBp: 'mmHg', DiastolicBp: 'mmHg', Pulse: 'bpm', Temperature: '°C', OxygenSaturation: '%', Weight: 'kg' };

export type VitalView = {
  kind: VitalKind; value: number; flag: VitalAlert | null; previousValue: number | null; previousAt: string | null; flagKept: boolean; recordedAt: string;
};

export type IntakeView = {
  appointmentId: string; patientId: string; facilityId: string; state: string; chiefComplaint: string | null;
  vitals: VitalView[]; escalated: boolean; handedOff: boolean; blockers: string[];
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
  SystolicBp: reading, DiastolicBp: reading, Pulse: reading, Temperature: reading, OxygenSaturation: reading, Weight: reading,
});
export type IntakeForm = z.infer<typeof intakeFormSchema>;

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
