// A patient's past visits (MDV-1088). The server decides every access: the chart's Visits carry clinical content, the desk's carry a date and a state only.
export type VisitRow = {
  encounterId: string; startedAt: string; status: 'Active' | 'Ended'; doctor: string | null; noteSigned: boolean;
  diagnoses: { description: string; code: string | null }[]; medicines: { name: string; dose: string | null }[]; tests: string[];
};
export type VisitsAnswer = { visits: VisitRow[]; timeZone: string };

/** `state` is the appointment state; the screen translates it (`schedule.state.<state>`). */
export type DeskVisitState = 'Booked' | 'CheckedIn' | 'WithNurse' | 'ReadyForDoctor' | 'InConsult' | 'Billing' | 'Done' | 'Cancelled' | 'NoShow' | 'NeedsRebooking' | 'LeftWithoutBeingSeen';
export type DeskVisit = { appointmentId: string; start: string; state: DeskVisitState };

/** What a visit is, in words: a signed note, a visit still open, or one ended with no signed note. Never colour alone. */
export const visitStatus = (v: Pick<VisitRow, 'status' | 'noteSigned'>) => (v.noteSigned ? 'signed' : v.status === 'Active' ? 'open' : 'unsigned') as 'signed' | 'open' | 'unsigned';
