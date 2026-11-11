import { describe, expect, it } from 'vitest';
import { remainingItems, signChecklist, type ChecklistInput } from './checklist';
import { signFailure } from './note';

const ready: ChecklistInput = {
  identityShown: true, allergyState: 'NoKnownAllergies', reconciliation: { confirmedAt: null, total: 0 }, recordingConsent: 'Declined', undecidedAi: [],
  sections: { chiefComplaint: 'Thirst', assessment: 'Review', plan: 'Rest' },
};
const state = (i: ChecklistInput) => Object.fromEntries(signChecklist(i).map(x => [x.id, x.state]));

describe('review and sign checklist (MDV-P2-044)', () => {
  it('is complete for a reviewed note and lists the eight items in order', () => {
    expect(signChecklist(ready).map(x => x.id)).toEqual(['identity', 'allergies', 'medicines', 'consent', 'history', 'ai', 'diagnoses', 'plan']);
    expect(remainingItems(signChecklist(ready))).toBe(0);
  });

  it('counts unrecorded consent, blank required sections and an unread reconciliation', () => {
    const items = signChecklist({ ...ready, recordingConsent: null, sections: { chiefComplaint: ' ' }, reconciliation: null });
    expect(state({ ...ready, recordingConsent: null, sections: { chiefComplaint: ' ' }, reconciliation: null }))
      .toMatchObject({ consent: 'todo', history: 'todo', diagnoses: 'todo', plan: 'todo', medicines: 'todo', identity: 'done' });
    expect(remainingItems(items)).toBe(5);
  });

  it('a reconciliation with medicines is done only once confirmed', () => {
    expect(state({ ...ready, reconciliation: { confirmedAt: null, total: 2 } }).medicines).toBe('todo');
    expect(state({ ...ready, reconciliation: { confirmedAt: '2026-11-12T05:00:00Z', total: 2 } }).medicines).toBe('done');
  });

  it('an undecided AI draft locks signing and its section stays unreviewed', () => {
    const s = state({ ...ready, undecidedAi: ['subjective', 'plan'] });
    expect(s).toMatchObject({ ai: 'todo', history: 'todo', plan: 'todo', diagnoses: 'done' });
    expect(remainingItems(signChecklist({ ...ready, undecidedAi: ['subjective', 'plan'] }))).toBe(3);
  });

  it('allergies not recorded are said but do not lock signing; withdrawn consent is a recorded answer', () => {
    const items = signChecklist({ ...ready, allergyState: 'NotRecorded', recordingConsent: 'Withdrawn' });
    expect(state({ ...ready, allergyState: 'NotRecorded' }).allergies).toBe('warn');
    expect(remainingItems(items)).toBe(0);
  });

  it('reads the server\'s checklist gates as unreviewed, blank sections first', () => {
    expect(signFailure(400, { errors: { recordingConsent: ['required'] } })).toEqual({ kind: 'unreviewed' });
    expect(signFailure(400, { errors: { undecidedAi: ['undecided'] } })).toEqual({ kind: 'unreviewed' });
    expect(signFailure(400, { errors: { 'sections.plan': ['required'], recordingConsent: ['required'] } })).toEqual({ kind: 'blank', sections: ['plan'] });
  });
});
