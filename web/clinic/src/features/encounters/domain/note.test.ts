import { describe, expect, it } from 'vitest';
import { missingRequired, reapply, staleVersion, switchChoices, switchTemplate, type NoteTemplate } from './note';

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
