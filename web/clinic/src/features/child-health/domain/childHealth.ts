import { z } from 'zod';

// Immunisations and developmental milestones (MDV-P2-036, E-01). The server works out every state from the date of birth on the facility's day
// and validates every record; this file names what it answers and shapes the forms.
export type DoseState = 'Given' | 'Overdue' | 'DueNow' | 'Upcoming';
export type MilestoneAnswer = 'Yes' | 'No' | 'Unsure';
export type MilestoneAnswerer = 'Parent' | 'Clinician';
// The codes the server's schedules and milestone bands use (ImmunisationSchedule, DevelopmentalMilestones); each has a translation.
export type BandKey = 'birth' | 'w6' | 'w10' | 'w14' | 'm6to9' | 'm9' | 'm12to18' | 'm16to18' | 'y4to6' | 'm2' | 'm4' | 'm6';
export type VaccineCode = 'BCG' | 'OPV' | 'HepB' | 'DTP' | 'IPV' | 'Hib' | 'Rotavirus' | 'PCV' | 'Influenza' | 'TCV' | 'MMR' | 'HepA' | 'Varicella' | 'Hexavalent';
export type MilestoneBandKey = 'y4';
export type MilestoneKey = 'hopsOnOneFoot' | 'tellsAStory' | 'drawsAPerson' | 'dressesWithLittleHelp' | 'playsCooperatively';
export const answers: MilestoneAnswer[] = ['Yes', 'No', 'Unsure'];
export const answerers: MilestoneAnswerer[] = ['Parent', 'Clinician'];

export type Dose = {
  vaccine: VaccineCode; dose: number; state: DoseState; dueFrom: string; overdueFrom: string; givenOn: string | null; batch: string | null; by: string | null;
  deferReason: string | null; deferredOn: string | null;
};
export type Band = { key: BandKey; doses: Dose[] };
export type Milestone = { key: MilestoneKey; answer: MilestoneAnswer | null; answeredBy: MilestoneAnswerer | null; recordedBy: string | null; recordedAt: string | null };
export type ChildHealth = {
  child: boolean; schedule: string; signedOff: boolean; bands: Band[];
  milestones: { key: MilestoneBandKey; milestones: Milestone[]; signedOff: boolean } | null;
};

const order: Record<DoseState, number> = { Overdue: 0, DueNow: 1, Upcoming: 2, Given: 3 };
/** A band reads as its most urgent dose: one overdue dose makes the band overdue; all given makes it given. */
export const bandState = (b: Band): DoseState => b.doses.reduce<DoseState>((worst, d) => (order[d.state] < order[worst] ? d.state : worst), 'Given');

/** The doses still to record (not given), as "vaccine|dose" keys for the form's picker. */
export const openDoses = (bands: Band[]) => bands.flatMap(b => b.doses.filter(d => d.state !== 'Given')).map(d => ({ key: `${d.vaccine}|${d.dose}`, vaccine: d.vaccine, dose: d.dose }));

// A deferral needs its reason and a given dose its day: said here first, refused by the server too.
export const doseSchema = z.object({
  dose: z.string().min(1),
  outcome: z.enum(['Given', 'Deferred']),
  givenOn: z.string(),
  batch: z.string().trim().max(50),
  givenBy: z.string().trim().max(100),
  reason: z.string().trim().max(500),
})
  .refine(d => d.outcome !== 'Given' || d.givenOn !== '', { path: ['givenOn'], message: 'required' })
  .refine(d => d.outcome !== 'Deferred' || d.reason !== '', { path: ['reason'], message: 'required' });
export type DoseDraft = z.infer<typeof doseSchema>;
export const emptyDose: DoseDraft = { outcome: 'Given', dose: '', givenOn: '', batch: '', givenBy: '', reason: '' };

/** The body the server takes: a given dose sends its day, batch and giver; a deferral only its reason. Blank stays null, never guessed. */
export function doseBody(d: DoseDraft) {
  const [vaccine, dose] = d.dose.split('|');
  const blank = (s: string) => (s.trim() === '' ? null : s.trim());
  return d.outcome === 'Given'
    ? { vaccine, dose: Number(dose), outcome: d.outcome, givenOn: d.givenOn, batch: blank(d.batch), givenBy: blank(d.givenBy), reason: null }
    : { vaccine, dose: Number(dose), outcome: d.outcome, givenOn: null, batch: null, givenBy: null, reason: d.reason.trim() };
}

export type Problem = 'already_given' | 'not_a_child' | 'invalid' | 'refused' | 'failed';
export function problemOf(status: number | undefined, code: string | undefined): Problem {
  if (code === 'already_given' || code === 'not_a_child') return code;
  if (status === 400) return 'invalid';
  if (status === 403) return 'refused';
  return 'failed';
}
