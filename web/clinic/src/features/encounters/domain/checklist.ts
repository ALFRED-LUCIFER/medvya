import type { MessageKey } from '@medvya/i18n';
import type { AllergyState } from '../../patient360/domain/chart';
import { isBlank, structuredIds, type NoteSections, type NoteTemplate, type RecordingConsent } from './note';

// The review-and-sign checklist (MDV-P2-044, E-04/E-06). Worked out from the note, the encounter banner and the reconciliation every time;
// nothing of it is stored. The server refuses a signature without a recorded consent, with an undecided AI draft or a blank required section,
// so the locked button is not the only control. Identity, allergies and medicines are checked here only (the server has no gate for them).
export const checklistIds = ['identity', 'allergies', 'medicines', 'consent', 'history', 'ai', 'diagnoses', 'plan'] as const;
export type ChecklistId = (typeof checklistIds)[number];
/** todo counts against signing; warn is said in words but does not lock (allergies not recorded: there is no way to record them in the consultation yet). */
export type ChecklistState = 'done' | 'todo' | 'warn';
export type ChecklistItem = { id: ChecklistId; state: ChecklistState };

export type ChecklistInput = {
  /** The banner with the patient's name and date of birth is on screen; the sign dialog repeats them. */
  identityShown: boolean;
  allergyState: AllergyState | null;
  /** The visit's reconciliation; null when it could not be read. */
  reconciliation: { confirmedAt: string | null; total: number } | null;
  recordingConsent: RecordingConsent | null;
  /** Section ids holding an AI draft or suggestion not yet accepted or dismissed (none before MDV-P3). */
  undecidedAi: string[];
  sections: NoteSections;
};

// The SOAP sections every template has (NoteTemplates.Soap): history is the chief complaint and subjective, diagnoses the assessment.
const historySections = ['chiefComplaint', 'subjective'];

/** A section group is reviewed when its key section has text and none of its sections holds an undecided AI draft. */
const reviewed = (sections: NoteSections, undecidedAi: string[], key: string, ids: string[]) => !isBlank(sections[key]) && !ids.some(id => undecidedAi.includes(id));

export function signChecklist(i: ChecklistInput): ChecklistItem[] {
  const done: Record<ChecklistId, boolean> = {
    identity: i.identityShown,
    allergies: i.allergyState !== null && i.allergyState !== 'NotRecorded',
    medicines: !!i.reconciliation && (i.reconciliation.confirmedAt !== null || i.reconciliation.total === 0),
    consent: i.recordingConsent !== null,
    history: reviewed(i.sections, i.undecidedAi, 'chiefComplaint', historySections),
    ai: i.undecidedAi.length === 0,
    diagnoses: reviewed(i.sections, i.undecidedAi, 'assessment', ['assessment']),
    plan: reviewed(i.sections, i.undecidedAi, 'plan', ['plan']),
  };
  return checklistIds.map(id => ({ id, state: done[id] ? 'done' : id === 'allergies' ? 'warn' : 'todo' }));
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
