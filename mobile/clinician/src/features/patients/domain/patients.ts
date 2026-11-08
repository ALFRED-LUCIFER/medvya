import type { Sex } from '../../today/domain/today';

export type PatientFlag = 'allergy' | 'overdue' | 'new' | 'critical';
/** Search result: only what a list needs. The phone is masked to its last four digits (privacy-safe search). */
export type PatientListItem = { id: string; name: string; initials: string; age: number; sex: Sex; phoneLast4: string; mrnLast4: string; flag?: PatientFlag };

export type Finding = { text: string; status: 'verified' | 'aiUnreviewed' };
export type Vital = { label: string; value: string; note: string; trend?: 'up' | 'down' };
export type PatientRecord = PatientListItem & {
  dateOfBirth: string;
  allergies: { substance: string; reaction: string; verified: boolean }[];
  vitals: Vital[];
  sinceLastVisit: Finding[];
  problems: string[];
  medicines: string[];
};

/** Matches name words, the last four phone digits or the last four MRN digits; never the full number. */
export function matchPatient(p: PatientListItem, query: string) {
  const q = query.trim().toLowerCase();
  if (!q) return true;
  if (/^\d{1,4}$/.test(q)) return p.phoneLast4.endsWith(q) || p.mrnLast4.endsWith(q);
  return p.name.toLowerCase().split(/\s+/).some(w => w.startsWith(q)) || p.name.toLowerCase().startsWith(q);
}
