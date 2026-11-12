import { z } from 'zod';

// Medication reconciliation (MDV-P2-011, E-07). The server builds the table and decides what is allowed; this file shapes the draft form and reads answers.
export type DecisionKind = 'Keep' | 'Change' | 'Taper' | 'Stop' | 'Add' | 'Ignore';
/** One step of a taper (MDV-P2-043): the dose as the doctor writes it, for this many days. */
export type TaperStep = { dose: string; days: number };
export type MedicineConflict = 'State' | 'Dose' | 'NotOnChart';
export type MedicineSide = { dose: string | null; frequency: string | null; state: 'Active' | 'Stopped'; needsClarification: boolean };
export type MedicineRow = {
  key: string; name: string; chart: MedicineSide | null; patient: MedicineSide | null; other: MedicineSide | null; conflict: MedicineConflict | null;
  allowed: DecisionKind[];
  decision: { kind: DecisionKind; newDose: string | null; reason: string | null; decidedAt: string; fromDose: string | null; steps: TaperStep[] | null; endsOn: string | null } | null;
};
export type Reconciliation = {
  encounterId: string; patientId: string; rows: MedicineRow[]; decided: number; total: number; disagree: number; confirmedAt: string | null;
};

/** Stop, Change and Taper are asked why (Change also to what, Taper its steps); the others are one click. */
export const needsDetail = (kind: DecisionKind) => kind === 'Stop' || kind === 'Change' || kind === 'Taper';

/** Confirm is possible only when every row has a decision (the server refuses it otherwise too). */
export const canConfirm = (r: Reconciliation) => r.confirmedAt === null && r.decided === r.total;

// Mirrors MedicationReconciliationRules (MaxSteps, MaxTaperDays); the server checks again.
export const maxSteps = 8, maxTaperDays = 365;
const stepSchema = z.object({ dose: z.string().trim().max(100), days: z.string().trim() });
export const draftSchema = z.object({ newDose: z.string().trim().max(100), reason: z.string().trim().max(500), steps: z.array(stepSchema) });
export type Draft = z.infer<typeof draftSchema>;
export const emptyStep = { dose: '', days: '' };
export const emptyDraft: Draft = { newDose: '', reason: '', steps: [emptyStep, emptyStep] };

/** A taper's steps as the server takes them: whole days, blank rows left out. */
export const toSteps = (d: Draft): TaperStep[] =>
  d.steps.filter(s => s.dose.trim() !== '' || s.days.trim() !== '').map(s => ({ dose: s.dose.trim(), days: /^\d{1,3}$/.test(s.days.trim()) ? Number(s.days.trim()) : 0 }));

/** Field codes a draft would be refused with, so the screen can say so before asking the server. */
export function draftProblems(kind: DecisionKind, d: Draft): ('reason' | 'newDose' | 'steps')[] {
  const steps = toSteps(d);
  const badSteps = kind === 'Taper' && (steps.length === 0 || steps.length > maxSteps || steps.some(s => s.dose === '' || s.days < 1) || steps.reduce((n, s) => n + s.days, 0) > maxTaperDays);
  return [...(needsDetail(kind) && d.reason.trim() === '' ? ['reason' as const] : []), ...(kind === 'Change' && d.newDose.trim() === '' ? ['newDose' as const] : []), ...(badSteps ? ['steps' as const] : [])];
}

/** The side another source holds against the chart, for the conflict sentence. */
export const disagreeing = (r: MedicineRow) => (r.patient ? ({ who: 'patient', side: r.patient } as const) : ({ who: 'other', side: r.other } as const));

export type Problem = 'undecided' | 'ended' | 'confirmed' | 'invalid' | 'failed';
/** The server's 409 code, or a rejected field ("reason:required"), as one of the messages the screen has. */
export const problemOf = (code: string | undefined): Problem =>
  code === 'undecided' || code === 'ended' || code === 'confirmed' ? code : code?.includes(':') ? 'invalid' : 'failed';
