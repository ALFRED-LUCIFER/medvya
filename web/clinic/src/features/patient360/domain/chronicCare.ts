import { z } from 'zod';

// Chronic care targets, care gaps and goals (MDV-P2-038): what the server answers. The server works out every gap and every "met"; nothing here decides one.
export type TargetCode = 'HbA1c' | 'BloodPressureSystolic' | 'BloodPressureDiastolic' | 'LdlCholesterol' | 'Egfr' | 'UrineAlbuminCreatinineRatio';
export type CareTarget = {
  code: TargetCode; unit: string; direction: 'Below' | 'AtLeast'; limit: number; isDefault: boolean;
  latest: number | null; latestAt: string | null; met: boolean | null; setBy: string | null; setAt: string | null;
};
export type GapRule = 'DiabeticEyeExam' | 'DiabeticFootExam' | 'UrineAlbumin' | 'OrderNotResulted' | 'VaccineDue' | 'GlucoseToleranceTest' | 'FallsReview';
export type GapAction = 'AddToVisit' | 'Order' | 'Refer';
/** `item` is the English text the action writes into the consultation (plan line, test, referral reason), the language of the printed papers. */
export type CareGap = {
  rule: GapRule; action: GapAction; item: string; lastDone: string | null; since: string | null; vaccine: string | null; dose: number | null; referTo: string | null;
};
export type CareGoal = { id: string; text: string; target: string | null; progressPercent: number; createdBy: string | null; createdAt: string; updatedBy: string | null; updatedAt: string };
export type ChronicCare = { chronic: boolean; targets: CareTarget[]; gaps: CareGap[]; goals: CareGoal[]; rulesSignedOff: boolean; timeZone: string };

/** What a gap's action came to: added to the open consultation, no consultation open, the note is signed, a save clashed, or it failed. */
export type GapOutcome = 'added' | 'noConsult' | 'signed' | 'conflict' | 'failed';

export const gapKey = (g: CareGap) => `${g.rule}:${g.item}`;

/** The eye and foot gaps close only on a recorded check; these are the two the doctor can record from the chart. */
export const checkKindOf = (g: CareGap) => (g.rule === 'DiabeticEyeExam' || g.rule === 'DiabeticFootExam' ? g.rule : null);

/** The plan section with the gap's line added once: a line already there is not repeated. */
export function withPlanLine(plan: string | undefined, line: string): string {
  const text = plan?.trimEnd() ?? '';
  return text.split('\n').some(l => l.trim() === line) ? text : text ? `${text}\n${line}` : line;
}

// The forms keep what was typed. The server's ranges are not repeated here: it refuses a value out of range and says which field.
const decimal = z.string().trim().refine(v => /^\d{1,5}([.,]\d{1,2})?$/.test(v), 'invalid');
const percent = z.string().trim().refine(v => v === '' || (/^\d{1,3}$/.test(v) && +v <= 100), 'out_of_range');

export const targetFormSchema = z.object({ limit: decimal });
export type TargetForm = z.infer<typeof targetFormSchema>;
export const toLimit = (f: TargetForm) => Number(f.limit.trim().replace(',', '.'));

export const checkFormSchema = z.object({ doneOn: z.string().regex(/^\d{4}-\d{2}-\d{2}$/, 'required') });
export type CheckForm = z.infer<typeof checkFormSchema>;

export const goalFormSchema = z.object({
  text: z.string().trim().min(1, 'required').max(200, 'too_long'),
  target: z.string().trim().max(200, 'too_long'),
  progress: percent,
});
export type GoalForm = z.infer<typeof goalFormSchema>;
export const emptyGoal: GoalForm = { text: '', target: '', progress: '' };
export const toGoalBody = (f: GoalForm) => ({ text: f.text.trim(), target: f.target.trim() || null, progressPercent: f.progress.trim() === '' ? 0 : Number(f.progress) });

export const progressFormSchema = z.object({ progress: percent.refine(v => v !== '', 'out_of_range') });
export type ProgressForm = z.infer<typeof progressFormSchema>;

/** The form error codes (Zod messages), each a key under chronicCare.error. */
export type CareError = 'invalid' | 'required' | 'too_long' | 'out_of_range';
