import { z } from 'zod';

// Medication reconciliation (MDV-P2-011, E-07). The server builds the table and decides what is allowed; this file shapes the draft form and reads answers.
export type DecisionKind = 'Keep' | 'Change' | 'Stop' | 'Add' | 'Ignore';
export type MedicineConflict = 'State' | 'Dose' | 'NotOnChart';
export type MedicineSide = { dose: string | null; frequency: string | null; state: 'Active' | 'Stopped'; needsClarification: boolean };
export type MedicineRow = {
  key: string; name: string; chart: MedicineSide | null; patient: MedicineSide | null; other: MedicineSide | null; conflict: MedicineConflict | null;
  allowed: DecisionKind[]; decision: { kind: DecisionKind; newDose: string | null; reason: string | null; decidedAt: string } | null;
};
export type Reconciliation = {
  encounterId: string; patientId: string; rows: MedicineRow[]; decided: number; total: number; disagree: number; confirmedAt: string | null;
};

/** Stop and Change are asked why (Change also to what); the others are one click. */
export const needsDetail = (kind: DecisionKind) => kind === 'Stop' || kind === 'Change';

/** Confirm is possible only when every row has a decision (the server refuses it otherwise too). */
export const canConfirm = (r: Reconciliation) => r.confirmedAt === null && r.decided === r.total;

export const draftSchema = z.object({ newDose: z.string().trim().max(100), reason: z.string().trim().max(500) });
export type Draft = z.infer<typeof draftSchema>;
export const emptyDraft: Draft = { newDose: '', reason: '' };

/** Field codes a draft would be refused with, so the screen can say so before asking the server. */
export function draftProblems(kind: DecisionKind, d: Draft): ('reason' | 'newDose')[] {
  return [...(needsDetail(kind) && d.reason.trim() === '' ? ['reason' as const] : []), ...(kind === 'Change' && d.newDose.trim() === '' ? ['newDose' as const] : [])];
}

/** The side another source holds against the chart, for the conflict sentence. */
export const disagreeing = (r: MedicineRow) => (r.patient ? ({ who: 'patient', side: r.patient } as const) : ({ who: 'other', side: r.other } as const));

export type Problem = 'undecided' | 'ended' | 'confirmed' | 'invalid' | 'failed';
/** The server's 409 code, or a rejected field ("reason:required"), as one of the messages the screen has. */
export const problemOf = (code: string | undefined): Problem =>
  code === 'undecided' || code === 'ended' || code === 'confirmed' ? code : code?.includes(':') ? 'invalid' : 'failed';
