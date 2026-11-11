import { z } from 'zod';
import type { MessageKey } from '@medvya/i18n';

// The consultation note draft (MDV-P2-009) and its templates (MDV-P2-025): what the server answers. The server decides access, structure and
// versions; these rules only save a round trip and keep the doctor's text safe.
export type NoteSection = { id: string; required: boolean };
export type NoteTemplate = { key: string; version: number; sections: NoteSection[]; quickPhrases: string[] };
export type NoteCatalog = { templates: NoteTemplate[]; mine: string[] };
export type NoteSections = Record<string, string>;
/** Ids only: who signed, when, and the hash of what was signed (MDV-P2-012). */
export type NoteSignature = { versionNo: number; signedBy: string; signedAt: string; contentHash: string };
/** A signed addendum (MDV-P2-013): version 2 and later, each following the version before it. */
export type NoteAddendum = { versionNo: number; amendsVersionNo: number; reason: string; text: string; signedBy: string; signedAt: string; contentHash: string };
export type NoteView = {
  encounterId: string; templateKey: string; templateVersion: number; status: 'Draft' | 'Signed'; manual: boolean;
  sections: NoteSections; version: number; updatedAt: string | null; signature?: NoteSignature | null; addenda?: NoteAddendum[];
};
/** What an autosave sends besides the version: the template and the whole note (a section left out is cleared). */
export type NoteDraft = { templateKey: string; sections: NoteSections };

export type SavePhase = 'idle' | 'saving' | 'saved' | 'failed' | 'conflict';

export const maxSectionLength = 10_000;
export const autosaveDelayMs = 1500;
export const noteFormSchema = z.object({ sections: z.record(z.string(), z.string().max(maxSectionLength)) });
export type NoteForm = z.infer<typeof noteFormSchema>;
export const templateChoiceSchema = z.object({ keys: z.array(z.string()).min(1) });
export type TemplateChoiceForm = z.infer<typeof templateChoiceSchema>;

export const templateNameKey = (key: string) => `noteTemplates.name.${key}` as MessageKey;
export const sectionLabelKey = (id: string) => `noteTemplates.section.${id}` as MessageKey;

const text = (s: NoteSections, id: string) => s[id] ?? '';
export const isBlank = (s: string | undefined) => !s?.trim();

/** Required sections still empty: marked on screen, never a block (a doctor may sign what they judge complete, MDV-P2-012). */
export const missingRequired = (template: NoteTemplate, sections: NoteSections) => template.sections.filter(s => s.required && isBlank(sections[s.id])).map(s => s.id);

/** Switching keeps text in sections both templates have and names the sections whose text the new template has no place for. */
export function switchTemplate(sections: NoteSections, to: NoteTemplate) {
  const ids = new Set(to.sections.map(s => s.id));
  const entries = Object.entries(sections).filter(([, v]) => !isBlank(v));
  return { kept: Object.fromEntries(entries.filter(([id]) => ids.has(id))), dropped: entries.map(([id]) => id).filter(id => !ids.has(id)) };
}

/** After a stale save: the doctor's changes since `base` laid over the version that is now on the server, section by section. */
export function reapply(base: NoteSections, mine: NoteSections, theirs: NoteSections): NoteSections {
  const out: NoteSections = {};
  for (const id of new Set([...Object.keys(base), ...Object.keys(mine), ...Object.keys(theirs)])) {
    const v = text(mine, id) !== text(base, id) ? text(mine, id) : text(theirs, id);
    if (v !== '') out[id] = v;
  }
  return out;
}

/** The server's version when a save lost the race (409 stale_note), else null. */
export function staleVersion(status: number | undefined, body: unknown): number | null {
  const b = (typeof body === 'object' && body !== null ? body : {}) as { code?: unknown; currentVersion?: unknown };
  return status === 409 && b.code === 'stale_note' && typeof b.currentVersion === 'number' ? b.currentVersion : null;
}

/** The templates a doctor can switch to: their choice, plus the one this note is already on. */
export const switchChoices = (catalog: NoteCatalog, current: string) => catalog.templates.filter(t => catalog.mine.includes(t.key) || t.key === current);

/** Why a sign call was refused, from the status and problem body: what the doctor sees or does next (MDV-P2-012). */
export type SignFailure = { kind: 'reauth' | 'stale' | 'signed' | 'denied' | 'failed' } | { kind: 'blank'; sections: string[] };

export function signFailure(status: number | undefined, body: unknown): SignFailure {
  const b = (typeof body === 'object' && body !== null ? body : {}) as { code?: unknown; errors?: Record<string, string[]> };
  if (status === 403) return { kind: b.code === 'reauth_required' ? 'reauth' : 'denied' };
  if (status === 409) return { kind: b.code === 'stale_note' ? 'stale' : b.code === 'note_signed' ? 'signed' : 'failed' };
  const blank = Object.keys(b.errors ?? {}).filter(k => k.startsWith('sections.')).map(k => k.slice('sections.'.length));
  return status === 400 && blank.length > 0 ? { kind: 'blank', sections: blank } : { kind: 'failed' };
}

/** Section ids in the template's order, so the first one named is the first one on the page. */
export const inTemplateOrder = (template: NoteTemplate, ids: string[]) => template.sections.map(s => s.id).filter(id => ids.includes(id));

export const maxReasonLength = 500;
/** An addendum needs its text and why it is added (MDV-P2-013); the server checks the same. */
export const addendumSchema = z.object({ text: z.string().trim().min(1).max(maxSectionLength), reason: z.string().trim().min(1).max(maxReasonLength) });
export type AddendumForm = z.infer<typeof addendumSchema>;

/** Why an addendum was refused: confirm identity, another addendum won the same moment, not allowed, or anything else. */
export type AddendumFailure = 'reauth' | 'stale' | 'denied' | 'failed';
export function addendumFailure(status: number | undefined, body: unknown): AddendumFailure {
  const { kind } = signFailure(status, body);
  return kind === 'reauth' || kind === 'stale' || kind === 'denied' ? kind : 'failed';
}

/** The server PDF refuses text in a script it cannot show yet (422 pdf_unsupported_text): the print view shows it. */
export const pdfFailure = (status: number | undefined): 'unsupported' | 'failed' => (status === 422 ? 'unsupported' : 'failed');

// Verified prefill, quick phrases and earlier-note snippets (MDV-P2-009). Nothing is added unless the doctor presses a button; the server decides what is verified.
/** One verified nurse-intake reading of this visit with its source: who took it and when. `recordedByName` is null when unknown. */
export type VerifiedVital = { code: string; value: number; unit: string; takenAt: string; source: string; recordedByName: string | null };
/** A signed note of the same patient, offered as snippets. */
export type PreviousNote = { encounterId: string; signedAt: string; signedByName: string | null; templateKey: string; sections: NoteSections };

export const phraseKey = (key: string) => `noteTemplates.phrase.${key}` as MessageKey;
export const vitalNameKey = (code: string) => `noteAssist.vital.${code}` as MessageKey;
/** Quick phrases and snippets go to the section last edited, else the plan. */
export const defaultTargetSection = 'plan';

/** The sections of an earlier note that have text, in the current template's order first, then any the template has no place for. */
export function snippetSections(note: Pick<PreviousNote, 'sections'>, template: NoteTemplate) {
  const ids = Object.keys(note.sections).filter(id => !isBlank(note.sections[id]));
  const order = template.sections.map(s => s.id);
  return [...order.filter(id => ids.includes(id)), ...ids.filter(id => !order.includes(id))];
}

/**
 * Puts `snippet` where the cursor is (replacing a selection), on a new line unless the text before it already ends one. Null when the section
 * would pass its length limit: nothing is cut off silently.
 */
export function insertAt(text: string, start: number, end: number, snippet: string): { text: string; caret: number } | null {
  const from = Math.min(start, text.length);
  const to = Math.min(Math.max(end, from), text.length);
  const before = text.slice(0, from);
  const add = (before && !before.endsWith('\n') ? '\n' : '') + snippet;
  const next = before + add + text.slice(to);
  return next.length > maxSectionLength ? null : { text: next, caret: from + add.length };
}
