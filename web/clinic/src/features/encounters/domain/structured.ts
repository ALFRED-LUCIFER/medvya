import type { MessageKey } from '@medvya/i18n';
import type { NoteSection, NoteSections, NoteTemplate } from './note';

// Structured review of systems and examination (MDV-P2-042). A structured section's text is a JSON object from item id to
// { answer, note? } (StructuredAnswers on the server, which checks it and stores it in the template's order). An item not answered is absent:
// blank stays blank and nothing defaults to normal.
export const rosAnswers = ['yes', 'no'] as const;
export const examAnswers = ['normal', 'abnormal', 'notExamined'] as const;
export type StructuredAnswer = (typeof rosAnswers)[number] | (typeof examAnswers)[number];
export type ItemAnswer = { answer: StructuredAnswer; note?: string };
export type Answers = Record<string, ItemAnswer>;

export const isStructured = (s: NoteSection) => !!s.kind && s.kind !== 'Text';
export const answersFor = (s: NoteSection) => (s.kind === 'ReviewOfSystems' ? rosAnswers : examAnswers);
export const itemLabelKey = (id: string) => `noteTemplates.item.${id}` as MessageKey;
export const answerLabelKey = (a: StructuredAnswer) => `noteTemplates.answer.${a}` as MessageKey;

export function parseAnswers(text: string | undefined): Answers {
  if (!text?.trim()) return {};
  try {
    const v: unknown = JSON.parse(text);
    return typeof v === 'object' && v !== null && !Array.isArray(v) ? (v as Answers) : {};
  } catch { return {}; }
}

/** The text for a section, items in the template's order; '' when nothing is answered (the section is then blank). */
export function writeAnswers(section: NoteSection, answers: Answers): string {
  const out: Answers = {};
  for (const id of section.items ?? []) {
    const a = answers[id];
    if (a) out[id] = a.note?.trim() ? { answer: a.answer, note: a.note } : { answer: a.answer };
  }
  return Object.keys(out).length ? JSON.stringify(out) : '';
}

/** Pressing the chosen answer again clears it (blank); pressing another one changes it and keeps the note. */
export function toggle(answers: Answers, item: string, answer: StructuredAnswer): Answers {
  const { [item]: current, ...rest } = answers;
  return current?.answer === answer ? rest : { ...rest, [item]: { ...current, answer } };
}

export const needsNote = (a: ItemAnswer | undefined) => a?.answer === 'abnormal' && !a.note?.trim();

/** The answered items in the template's order: what the signed note and its print view show in words. */
export const answeredItems = (section: NoteSection, text: string | undefined) => {
  const answers = parseAnswers(text);
  return (section.items ?? []).filter(id => answers[id]).map(id => ({ id, ...answers[id]! }));
};

/** Abnormal findings still without a note, as `<section>.<item>`: the server refuses them, so they wait on screen and signing points to them. */
export const unfinishedFindings = (template: NoteTemplate, sections: NoteSections) =>
  template.sections.filter(isStructured).flatMap(s => answeredItems(s, sections[s.id]).filter(needsNote).map(a => `${s.id}.${a.id}`));

/** What an autosave sends: an abnormal finding is held back until it has its note (the server would refuse the whole save). */
export function forSave(template: NoteTemplate, sections: NoteSections): NoteSections {
  const out = { ...sections };
  for (const s of template.sections.filter(isStructured)) {
    const answers = parseAnswers(sections[s.id]);
    out[s.id] = writeAnswers(s, Object.fromEntries(Object.entries(answers).filter(([, a]) => !needsNote(a))));
  }
  return out;
}

/** A template switch keeps the answers to items the new template also asks; true when some were dropped. */
export function keepItems(section: NoteSection, text: string): { text: string; dropped: boolean } {
  const answers = parseAnswers(text);
  const next = writeAnswers(section, answers);
  return { text: next, dropped: Object.keys(answers).some(id => !(section.items ?? []).includes(id)) };
}
