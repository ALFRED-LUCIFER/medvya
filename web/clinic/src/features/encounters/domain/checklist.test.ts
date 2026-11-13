import { describe, expect, it } from 'vitest';
import { remainingItems, sectionTicks, signChecklist, templateOwnSections, type ChecklistInput } from './checklist';
import { signFailure } from './note';

const ready: ChecklistInput = {
  identityConfirmed: true, allergyState: 'NoKnownAllergies', reconciliation: { confirmedAt: null, total: 0 }, recordingConsent: 'Declined', undecidedAi: [],
  sections: { chiefComplaint: 'Thirst', assessment: 'Review', plan: 'Rest' }, diagnosisRecorded: true,
};
const state = (i: ChecklistInput) => Object.fromEntries(signChecklist(i).map(x => [x.id, x.state]));

describe('review and sign checklist (MDV-P2-044)', () => {
  it('is complete for a reviewed note and lists the six items in order, with no recording consent row until recording exists (MDV-AUDIT-1092) and no AI row until AI drafting exists (MDV-P1-091)', () => {
    expect(signChecklist(ready).map(x => x.id)).toEqual(['identity', 'allergies', 'medicines', 'history', 'diagnoses', 'plan']);
    expect(remainingItems(signChecklist(ready))).toBe(0);
  });

  it('counts blank required sections and an unread reconciliation', () => {
    const items = signChecklist({ ...ready, recordingConsent: null, sections: { chiefComplaint: ' ' }, reconciliation: null });
    expect(state({ ...ready, recordingConsent: null, sections: { chiefComplaint: ' ' }, reconciliation: null }))
      .toMatchObject({ history: 'todo', diagnoses: 'todo', plan: 'todo', medicines: 'todo', identity: 'done' });
    expect(remainingItems(items)).toBe(4);
  });

  it('a reconciliation with medicines is done only once confirmed', () => {
    expect(state({ ...ready, reconciliation: { confirmedAt: null, total: 2 } }).medicines).toBe('todo');
    expect(state({ ...ready, reconciliation: { confirmedAt: '2026-11-12T05:00:00Z', total: 2 } }).medicines).toBe('done');
  });

  // MDV-P1-091: the identity item reports only what happened. The banner is on screen on every consultation; only the doctor's stored
  // "Confirmed with the patient" makes the item done, and until then it counts against signing.
  it('identity is not done on a newly opened consultation, and is done only by the stored confirmation', () => {
    const opened = { ...ready, identityConfirmed: false };
    expect(state(opened).identity).toBe('todo');
    expect(remainingItems(signChecklist(opened))).toBe(1); // signing stays locked
    expect(state(ready).identity).toBe('done');
    expect(remainingItems(signChecklist(ready))).toBe(0);
  });

  it('no AI line while the clinic has no AI drafting; one the server reports undecided is still shown and locks signing', () => {
    expect(signChecklist(ready).some(x => x.id === 'ai')).toBe(false);
    expect(state({ ...ready, undecidedAi: ['plan'] }).ai).toBe('todo');
  });

  it('an undecided AI draft locks signing and its section stays unreviewed', () => {
    const s = state({ ...ready, undecidedAi: ['subjective', 'plan'] });
    expect(s).toMatchObject({ ai: 'todo', history: 'todo', plan: 'todo', diagnoses: 'done' });
    expect(remainingItems(signChecklist({ ...ready, undecidedAi: ['subjective', 'plan'] }))).toBe(3);
  });

  it('allergies not recorded lock signing, so the sign message never says ready beside them (MDV audit P0-2)', () => {
    expect(state({ ...ready, allergyState: 'NotRecorded' }).allergies).toBe('todo');
    expect(state({ ...ready, allergyState: null }).allergies).toBe('todo');
    expect(remainingItems(signChecklist({ ...ready, allergyState: 'NotRecorded' }))).toBe(1);
    expect(state({ ...ready, allergyState: 'NoKnownAllergies' }).allergies).toBe('done');
  });

  it('a missing recording consent never counts against signing (MDV-AUDIT-1092)', () => {
    expect(remainingItems(signChecklist({ ...ready, recordingConsent: null }))).toBe(0);
  });

  it('diagnoses are done only with a diagnosis on the problem list; an assessment without one is said, not locked (MDV audit UX-11)', () => {
    expect(state({ ...ready, diagnosisRecorded: false }).diagnoses).toBe('warn');
    expect(state({ ...ready, diagnosisRecorded: null }).diagnoses).toBe('warn');
    expect(remainingItems(signChecklist({ ...ready, diagnosisRecorded: false }))).toBe(0);
    expect(state({ ...ready, diagnosisRecorded: false, sections: { chiefComplaint: 'Thirst', plan: 'Rest' } }).diagnoses).toBe('todo');
  });

  it('reads the server\'s checklist gates as unreviewed, blank sections first', () => {
    expect(signFailure(400, { errors: { recordingConsent: ['required'] } })).toEqual({ kind: 'unreviewed' });
    expect(signFailure(400, { errors: { undecidedAi: ['undecided'] } })).toEqual({ kind: 'unreviewed' });
    expect(signFailure(400, { errors: { 'sections.plan': ['required'], recordingConsent: ['required'] } })).toEqual({ kind: 'blank', sections: ['plan'] });
  });

  it('ticks a numbered section once it is finished; an AI draft still undecided leaves it open', () => {
    const tpl = { key: 'diabetes-review', version: 1, quickPhrases: [], sections: ['chiefComplaint', 'subjective', 'selfMonitoring', 'objective', 'assessment', 'plan'].map(id => ({ id, required: false })) };
    expect(templateOwnSections(tpl).map(s => s.id)).toEqual(['selfMonitoring']);
    const base = { template: tpl, sections: { chiefComplaint: 'Thirst', objective: 'Feet normal', assessment: 'Review' }, undecidedAi: [], vitals: 0, signed: false };
    expect(sectionTicks(base)).toEqual({ vitals: false, history: true, template: false, exam: true, assessment: true, plan: false, sign: false });
    expect(sectionTicks({ ...base, vitals: 2, sections: { ...base.sections, selfMonitoring: 'Fasting 140', plan: 'Rest' }, undecidedAi: ['objective'], signed: true }))
      .toEqual({ vitals: true, history: true, template: true, exam: false, assessment: true, plan: true, sign: true });
    expect(sectionTicks({ ...base, template: { ...tpl, sections: tpl.sections.filter(s => s.id !== 'selfMonitoring') } }).template).toBe(true); // nothing of its own
  });
});
