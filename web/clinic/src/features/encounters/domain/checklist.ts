import type { MessageKey } from '@medvya/i18n';
import type { AllergyState } from '../../patient360/domain/chart';
import { aiDraftingAvailable, isBlank, recordingAvailable, structuredIds, type NoteSections, type NoteTemplate, type RecordingConsent } from './note';

// The review-and-sign checklist (MDV-P2-044, E-04/E-06). Worked out from the note, the encounter banner and the reconciliation every time;
// nothing of it is stored. The server refuses a signature without a recorded consent, with an undecided AI draft or a blank required section,
// so the locked button is not the only control; it also refuses one while allergies are not recorded (MDV audit P0-2). Identity and medicines
// are checked here only. Each item reports only what happened (MDV-P1-091): identity is done only by the doctor's stored confirmation.
export const checklistIds = ['identity', 'allergies', 'medicines', 'consent', 'history', 'ai', 'diagnoses', 'plan'] as const;
export type ChecklistId = (typeof checklistIds)[number];
/**
 * todo counts against signing; warn is said in words but does not lock (an assessment with no diagnosis on the problem list: a visit without a
 * diagnosis is allowed, but it is never shown as "Done", MDV audit UX-11). Allergies not recorded is todo: the doctor records them in the
 * consultation, and the sign message never says "ready" beside it (MDV audit P0-2).
 */
export type ChecklistState = 'done' | 'todo' | 'warn';
export type ChecklistItem = { id: ChecklistId; state: ChecklistState };

export type ChecklistInput = {
  /** The treating doctor pressed "Confirmed with the patient" and the server stored who and when (MDV-P1-091); showing the banner is not a confirmation. */
  identityConfirmed: boolean;
  allergyState: AllergyState | null;
  /** The visit's reconciliation; null when it could not be read. */
  reconciliation: { confirmedAt: string | null; total: number } | null;
  recordingConsent: RecordingConsent | null;
  /** Section ids holding an AI draft or suggestion not yet accepted or dismissed (none before MDV-P3). */
  undecidedAi: string[];
  sections: NoteSections;
  /** The problem list holds a diagnosis (added at this visit, or an active one); null when the list could not be read. */
  diagnosisRecorded: boolean | null;
};

// The SOAP sections every template has (NoteTemplates.Soap): history is the chief complaint and subjective, diagnoses the assessment.
const historySections = ['chiefComplaint', 'subjective'];

/** A section group is reviewed when its key section has text and none of its sections holds an undecided AI draft. */
const reviewed = (sections: NoteSections, undecidedAi: string[], key: string, ids: string[]) => !isBlank(sections[key]) && !ids.some(id => undecidedAi.includes(id));

export function signChecklist(i: ChecklistInput): ChecklistItem[] {
  const done: Record<ChecklistId, boolean> = {
    identity: i.identityConfirmed,
    allergies: i.allergyState !== null && i.allergyState !== 'NotRecorded',
    medicines: !!i.reconciliation && (i.reconciliation.confirmedAt !== null || i.reconciliation.total === 0),
    consent: i.recordingConsent !== null,
    history: reviewed(i.sections, i.undecidedAi, 'chiefComplaint', historySections),
    ai: i.undecidedAi.length === 0,
    diagnoses: reviewed(i.sections, i.undecidedAi, 'assessment', ['assessment']),
    plan: reviewed(i.sections, i.undecidedAi, 'plan', ['plan']),
  };
  // The AI line only where AI drafting exists, or when the server reports an undecided AI draft anyway: never a Done for something that cannot happen.
  const shown = (id: ChecklistId) => (id !== 'consent' || recordingAvailable) && (id !== 'ai' || aiDraftingAvailable || i.undecidedAi.length > 0);
  return checklistIds.filter(shown).map(id => ({
    id,
    state: id === 'diagnoses' && done.diagnoses && !i.diagnosisRecorded ? 'warn' : done[id] ? 'done' : 'todo',
  }));
}

export const remainingItems = (items: ChecklistItem[]) => items.filter(x => x.state === 'todo').length;
export const checklistKey = (id: ChecklistId) => `signChecklist.item.${id}` as MessageKey;

// The one-page consultation (MDV-P2-041): numbered sections over today's template, each ticked once finished. The review of systems sits in
// History and the examination by system in Examination (MDV-P2-042); P2-043 (plan actions) fills Plan later.
export const studioSections = ['vitals', 'history', 'template', 'exam', 'assessment', 'plan', 'sign'] as const;
export type StudioSection = (typeof studioSections)[number];
const soapSections = ['chiefComplaint', 'subjective', 'objective', 'assessment', 'plan'];

/** The template's own sections (diabetes self-monitoring, antenatal obstetric history, ...): the Template section of the page. */
export const templateOwnSections = (template: NoteTemplate) => template.sections.filter(s => !soapSections.includes(s.id) && !structuredIds.includes(s.id));

export type TickInput = { template: NoteTemplate; sections: NoteSections; undecidedAi: string[]; vitals: number; signed: boolean };

/** Which numbered sections are finished: words and an icon on the section bar, never colour alone. */
export function sectionTicks(i: TickInput): Record<StudioSection, boolean> {
  const own = templateOwnSections(i.template);
  return {
    vitals: i.vitals > 0,
    history: reviewed(i.sections, i.undecidedAi, 'chiefComplaint', historySections),
    template: own.length === 0 || own.some(s => !isBlank(i.sections[s.id])),
    exam: (reviewed(i.sections, i.undecidedAi, 'objective', ['objective', 'examination']) || reviewed(i.sections, i.undecidedAi, 'examination', ['objective', 'examination'])),
    assessment: reviewed(i.sections, i.undecidedAi, 'assessment', ['assessment']),
    plan: reviewed(i.sections, i.undecidedAi, 'plan', ['plan']),
    sign: i.signed,
  };
}
