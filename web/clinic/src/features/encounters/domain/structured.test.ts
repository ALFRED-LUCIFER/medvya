import { describe, expect, it } from 'vitest';
import { sectionTicks, templateOwnSections } from './checklist';
import { snippetSections, switchTemplate, type NoteSection, type NoteTemplate } from './note';
import { answeredItems, forSave, parseAnswers, toggle, unfinishedFindings, writeAnswers } from './structured';

const ros: NoteSection = { id: 'reviewOfSystems', required: false, kind: 'ReviewOfSystems', items: ['tiredness', 'chestPain', 'fever'] };
const exam: NoteSection = { id: 'examination', required: false, kind: 'Examination', items: ['general', 'heart', 'feet'] };
const tpl = (key: string, ...sections: NoteSection[]): NoteTemplate => ({
  key, version: 2, quickPhrases: [],
  sections: [{ id: 'chiefComplaint', required: true }, { id: 'subjective', required: false }, ...sections, { id: 'objective', required: false }, { id: 'assessment', required: true }, { id: 'plan', required: true }],
});
const diabetes = tpl('diabetes-review', ros, { id: 'selfMonitoring', required: false }, exam);

describe('structured review of systems and examination (MDV-P2-042)', () => {
  it('writes answers in the template order, nothing for an unanswered item, and blank when none is answered', () => {
    expect(writeAnswers(ros, { fever: { answer: 'yes' }, tiredness: { answer: 'no' } })).toBe('{"tiredness":{"answer":"no"},"fever":{"answer":"yes"}}');
    expect(writeAnswers(exam, { heart: { answer: 'normal', note: '  ' } })).toBe('{"heart":{"answer":"normal"}}');
    expect(writeAnswers(ros, {})).toBe('');
    expect(answeredItems(ros, '{"fever":{"answer":"yes"}}')).toEqual([{ id: 'fever', answer: 'yes' }]);
    expect(parseAnswers('not json')).toEqual({});
  });

  it('pressing the chosen answer again clears it; another answer keeps the note', () => {
    const a = toggle({}, 'feet', 'abnormal');
    expect(a).toEqual({ feet: { answer: 'abnormal' } });
    expect(toggle(a, 'feet', 'abnormal')).toEqual({});
    expect(toggle({ feet: { answer: 'abnormal', note: 'Ulcer' } }, 'feet', 'normal')).toEqual({ feet: { answer: 'normal', note: 'Ulcer' } });
  });

  it('holds back an abnormal finding without its note from the save and points signing to it', () => {
    const sections = { examination: '{"heart":{"answer":"normal"},"feet":{"answer":"abnormal"}}', plan: 'Rest' };
    expect(forSave(diabetes, sections)).toEqual({ examination: '{"heart":{"answer":"normal"}}', reviewOfSystems: '', plan: 'Rest' });
    expect(unfinishedFindings(diabetes, sections)).toEqual(['examination.feet']);
    expect(unfinishedFindings(diabetes, { examination: '{"feet":{"answer":"abnormal","note":"Ulcer"}}' })).toEqual([]);
  });

  it('a template switch keeps answers the new template also asks and names the section when some are dropped', () => {
    const antenatal = tpl('antenatal', { ...ros, items: ['fever', 'palpitations'] }, exam);
    const r = switchTemplate({ reviewOfSystems: '{"tiredness":{"answer":"yes"},"fever":{"answer":"no"}}', examination: '{"heart":{"answer":"normal"}}' }, antenatal);
    expect(r.kept).toEqual({ reviewOfSystems: '{"fever":{"answer":"no"}}', examination: '{"heart":{"answer":"normal"}}' });
    expect(r.dropped).toEqual(['reviewOfSystems']);
    expect(switchTemplate({ examination: '{"heart":{"answer":"normal"}}' }, tpl('general-opd')).dropped).toEqual(['examination']);
  });

  it('places the structured sections in History and Examination, not the template section, and ticks Examination', () => {
    expect(templateOwnSections(diabetes).map(s => s.id)).toEqual(['selfMonitoring']);
    const ticks = (sections: Record<string, string>) => sectionTicks({ template: diabetes, sections, undecidedAi: [], vitals: 0, signed: false }).exam;
    expect(ticks({})).toBe(false);
    expect(ticks({ examination: '{"heart":{"answer":"normal"}}' })).toBe(true);
    expect(ticks({ objective: 'BP 130/80' })).toBe(true);
  });

  it('earlier answers are not offered as snippets', () => {
    expect(snippetSections({ sections: { plan: 'Rest', reviewOfSystems: '{"fever":{"answer":"yes"}}' } }, diabetes)).toEqual(['plan']);
  });
});
