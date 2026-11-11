import { describe, expect, it } from 'vitest';
import { insertAt, maxSectionLength, snippetSections, addendumFailure, addendumSchema, inTemplateOrder, missingRequired, pdfFailure, signFailure, reapply, staleVersion, switchChoices, switchTemplate, type NoteTemplate } from './note';

const tpl = (key: string, ...ids: string[]): NoteTemplate => ({ key, version: 1, quickPhrases: [], sections: ids.map(id => ({ id, required: id === 'plan' })) });

describe('note rules', () => {
  it('lists required sections that are blank, whitespace included', () => {
    expect(missingRequired(tpl('a', 'plan', 'objective'), { plan: '  ', objective: 'x' })).toEqual(['plan']);
    expect(missingRequired(tpl('a', 'plan'), { plan: 'rest' })).toEqual([]);
  });

  it('switching keeps matching sections and names the text with no place to go', () => {
    const r = switchTemplate({ plan: 'p', fetalWellbeing: 'f', objective: '  ' }, tpl('b', 'plan', 'objective'));
    expect(r).toEqual({ kept: { plan: 'p' }, dropped: ['fetalWellbeing'] });
  });

  it('reapply keeps my edits and takes theirs where I changed nothing', () => {
    expect(reapply({ plan: 'a', objective: 'o' }, { plan: 'mine', objective: 'o', subjective: 'new' }, { plan: 'theirs', objective: 'o2' }))
      .toEqual({ plan: 'mine', objective: 'o2', subjective: 'new' });
    expect(reapply({ plan: 'a' }, { plan: '' }, { plan: 'a' })).toEqual({}); // I cleared it on purpose
  });

  it('reads only a stale_note 409 as stale', () => {
    expect(staleVersion(409, { code: 'stale_note', currentVersion: 3 })).toBe(3);
    expect(staleVersion(409, { code: 'other' })).toBeNull();
    expect(staleVersion(500, { code: 'stale_note', currentVersion: 3 })).toBeNull();
  });

  it('offers the doctor\'s templates and the current one', () => {
    const catalog = { templates: [tpl('a'), tpl('b'), tpl('c')], mine: ['a'] };
    expect(switchChoices(catalog, 'c').map(t => t.key)).toEqual(['a', 'c']);
  });
});

describe('adding text to the note (MDV-P2-009)', () => {
  it('inserts at the cursor, replacing a selection, on its own line', () => {
    expect(insertAt('', 0, 0, 'Pulse 88')).toEqual({ text: 'Pulse 88', caret: 8 });
    expect(insertAt('Cough', 5, 5, 'Rest')).toEqual({ text: 'Cough\nRest', caret: 10 });
    expect(insertAt('Cough\n', 6, 6, 'Rest')).toEqual({ text: 'Cough\nRest', caret: 10 });
    expect(insertAt('abcXYZdef', 3, 6, 'Q')).toEqual({ text: 'abc\nQdef', caret: 5 });
    expect(insertAt('ab', 99, 99, 'c')?.text).toBe('ab\nc'); // a stale cursor lands at the end
  });

  it('refuses rather than cutting text off at the length limit', () => {
    expect(insertAt('x'.repeat(maxSectionLength), 0, 0, 'y')).toBeNull();
  });

  it('offers an earlier note\'s sections in the template order, skipping blanks', () => {
    const note = { sections: { plan: 'p', objective: '  ', chiefComplaint: 'c', old: 'o' } };
    expect(snippetSections(note, tpl('a', 'chiefComplaint', 'objective', 'plan'))).toEqual(['chiefComplaint', 'plan', 'old']);
  });
});

describe('sign failures (MDV-P2-012)', () => {
  it('tells a re-authentication from other refusals', () => {
    expect(signFailure(403, { code: 'reauth_required' })).toEqual({ kind: 'reauth' });
    expect(signFailure(403, { code: 'no_care_relationship' })).toEqual({ kind: 'denied' });
    expect(signFailure(403, null)).toEqual({ kind: 'denied' });
  });
  it('maps conflicts and blank required sections', () => {
    expect(signFailure(409, { code: 'stale_note', currentVersion: 3 })).toEqual({ kind: 'stale' });
    expect(signFailure(409, { code: 'note_signed' })).toEqual({ kind: 'signed' });
    expect(signFailure(400, { errors: { 'sections.plan': ['required'], 'sections.assessment': ['required'] } })).toEqual({ kind: 'blank', sections: ['plan', 'assessment'] });
    expect(signFailure(400, { errors: { body: ['required'] } })).toEqual({ kind: 'failed' });
    expect(signFailure(500, undefined)).toEqual({ kind: 'failed' });
  });
  it('names sections in the order of the template', () => {
    expect(inTemplateOrder(tpl('a', 'chiefComplaint', 'assessment', 'plan'), ['plan', 'chiefComplaint'])).toEqual(['chiefComplaint', 'plan']);
  });
});

describe('addendum rules (MDV-P2-013)', () => {
  it('needs a text and a reason, blanks included', () => {
    expect(addendumSchema.safeParse({ text: 'Dose corrected', reason: 'Typo' }).success).toBe(true);
    expect(addendumSchema.safeParse({ text: 'Dose corrected', reason: '   ' }).success).toBe(false);
    expect(addendumSchema.safeParse({ text: '', reason: 'Typo' }).success).toBe(false);
    expect(addendumSchema.safeParse({ text: 'x', reason: 'r'.repeat(501) }).success).toBe(false);
  });
  it('maps refusals to what the doctor sees', () => {
    expect(addendumFailure(403, { code: 'reauth_required' })).toBe('reauth');
    expect(addendumFailure(403, { code: 'no_care_relationship' })).toBe('denied');
    expect(addendumFailure(409, { code: 'stale_note', currentVersion: 4 })).toBe('stale');
    expect(addendumFailure(409, { code: 'note_not_signed' })).toBe('failed');
    expect(addendumFailure(400, { errors: { reason: ['required'] } })).toBe('failed');
    expect(pdfFailure(422)).toBe('unsupported');
    expect(pdfFailure(500)).toBe('failed');
  });
});
