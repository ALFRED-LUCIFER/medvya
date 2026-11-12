import { refusalOf, type ClinicalRefusal } from '../../clinical-access/domain/clinicalAccess';
import type { SexAtBirth } from '../../patients/domain/directory';
import type { Allergy, AllergyState } from '../../patient360/domain/chart';
import { dateIn } from '../../audit-log/domain/auditLog';

// Encounters (MDV-P2-001): what the server answers. The server decides every access; nothing here grants anything.
export type EncounterStatus = 'Active' | 'Ended';
export type Encounter = {
  id: string; appointmentId: string; patientId: string; facilityId: string; leadClinicianId: string;
  status: EncounterStatus; authorityMode: 'Medvya'; startedAt: string; endedAt: string | null; room: string | null;
};

/** The identity banner of every clinical screen: who the encounter is for, and their treatment consent. */
export type EncounterBanner = {
  encounter: Encounter;
  patient: { id: string; fullName: string; mrn: string; dateOfBirth: string; ageYears: number; sexAtBirth: SexAtBirth };
  treatmentConsent: boolean;
  /** The encounter facility's IANA zone: its times show in it, whatever the device's zone. */
  timeZone: string;
  /** Not recorded, no known allergies and listed are three different answers (never "none" for unknown). */
  allergies: { state: AllergyState; allergies: Allergy[] };
};

/** The consultation's two views (MDV-P2-041): one page, or the guided steps of before. Both edit the same note through the same autosave. */
export type ConsultView = 'page' | 'steps';
/** The one page's side panel: Assist (hidden when the patient declined recording), a chart summary, and the review-and-sign checklist. */
export type StudioPanel = 'assist' | 'chart' | 'check';

/** Still open after the facility's day it started on (MDV audit UX-11): nobody pressed End. Days are counted in the facility's time zone. */
export const openPastDay = (e: Pick<Encounter, 'status' | 'startedAt'>, timeZone: string, now = new Date()) =>
  e.status === 'Active' && dateIn(new Date(e.startedAt), timeZone) < dateIn(now, timeZone);

/** Visits a doctor can open a consultation for (the server's VisitFlow.CanStartConsult). */
export const consultStates = ['CheckedIn', 'WithNurse', 'ReadyForDoctor', 'InConsult'] as const;
export const canConsult = (state: string) => (consultStates as readonly string[]).includes(state);

/** A visit the board will not move into a consultation; anything unknown reads as "changed, try again". */
const visitProblems = ['not_arrived', 'finished', 'not_today', 'ended'] as const;
export type VisitProblem = (typeof visitProblems)[number] | 'changed';

/** Why a start did not go through, as the doctor must act on it. */
export type StartProblem =
  | { kind: 'open'; encounterId: string }
  | { kind: 'refused'; refusal: ClinicalRefusal }
  | { kind: 'visit'; code: VisitProblem }
  | { kind: 'failed' };

/** Reads a failed start: another open consultation (409 open_encounter), a clinical refusal (403), a visit that cannot move (409), or anything else. */
export function startProblem(status: number | undefined, body: unknown): StartProblem {
  const b = (typeof body === 'object' && body !== null ? body : {}) as { code?: unknown; encounterId?: unknown };
  if (status === 409 && b.code === 'open_encounter' && typeof b.encounterId === 'string') return { kind: 'open', encounterId: b.encounterId };
  if (status === 403) return { kind: 'refused', refusal: refusalOf(body) ?? { canRequest: false, requestPending: false } };
  if (status === 409 && typeof b.code === 'string') return { kind: 'visit', code: (visitProblems as readonly string[]).includes(b.code) ? (b.code as VisitProblem) : 'changed' };
  return { kind: 'failed' };
}

/** The one page's Plan heading id (MDV-P2-043): "Plan change" in the older adult medicine review (MDV-P2-037) links to it. */
export const planHeading = 'studio-plan';
