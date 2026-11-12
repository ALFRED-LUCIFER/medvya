// The plan's follow-up (MDV-P2-043): src/Modules/Encounters/Medvya.Encounters.Domain/FollowUp.cs and IFollowUps.cs. The server works out the due day.
export const followUpPeriods = ['P2W', 'P1M', 'P3M'] as const;
export type FollowUpPeriod = (typeof followUpPeriods)[number];
/** Where the front desk is with it (the waitlist's states); all null: no follow-up. */
export type FollowUp = { period: FollowUpPeriod | null; dueOn: string | null; state: 'Waiting' | 'Offered' | 'Booked' | null };

export type FollowUpProblem = 'follow_up_booked' | 'already_waiting' | 'ended' | 'failed';
const problems: readonly string[] = ['follow_up_booked', 'already_waiting', 'ended'];
export const followUpProblemOf = (code: string | undefined): FollowUpProblem => (code && problems.includes(code) ? (code as FollowUpProblem) : 'failed');
/** Picking the chosen period again takes the follow-up off the desk's list. */
export const nextPeriod = (current: FollowUpPeriod | null, picked: FollowUpPeriod): FollowUpPeriod | null => (current === picked ? null : picked);
