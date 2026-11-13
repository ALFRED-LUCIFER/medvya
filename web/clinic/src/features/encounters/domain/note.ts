import { z } from 'zod';
import type { MessageKey } from '@medvya/i18n';
import { isStructured, keepItems } from './structured';

// The consultation note draft (MDV-P2-009) and its templates (MDV-P2-025): what the server answers. The server decides access, structure and
// versions; these rules only save a round trip and keep the doctor's text safe.
/** A structured section (MDV-P2-042) lists its symptoms or body systems in `items`; its text is the answers (domain/structured). */
export type NoteSection = { id: string; required: boolean; kind?: 'Text' | 'ReviewOfSystems' | 'Examination'; items?: string[] | null };
export type NoteTemplate = { key: string; version: number; sections: NoteSection[]; quickPhrases: string[] };
export type NoteCatalog = { templates: NoteTemplate[]; mine: string[] };
export type NoteSections = Record<string, string>;
/** Who signed, when, and the hash of what was signed (MDV-P2-012); the name is the doctor profile's, else the account's, one name on every surface; the licence the owner attested is absent without a profile. */
export type NoteSignature = { versionNo: number; signedBy: string; signedAt: string; contentHash: string; signerName?: string | null; signerLicence?: string | null };
/** The note is for this person: what the sign modal repeats so a signature cannot land on the wrong chart. */
export type SignPatient = { name: string; mrn: string; dateOfBirth: string };
/** "Dr. {name}" without doubling a title the profile already carries. */
export const withTitle = (name: string) => (/^dr\.?\s/i.test(name) ? name : `Dr. ${name}`);
/** A signed addendum (MDV-P2-013): version 2 and later, each following the version before it. */
export type NoteAddendum = { versionNo: number; amendsVersionNo: number; reason: string; text: string; signedBy: string; signedAt: string; contentHash: string; signerName?: string | null };
/** shortcut: no recording exists before MDV-P3, so the consent step and its checklist row are hidden and signing never needs it (MDV-AUDIT-1092); flip to a tenant setting when recording ships. */
export const recordingAvailable = false;
/** The doctor's record of the patient's answer about recording this visit (MDV-P2-044); none yet means the note cannot be signed. */
export const recordingConsents = ['Agreed', 'Declined', 'Withdrawn'] as const;
export type RecordingConsent = (typeof recordingConsents)[number];
export type NoteView = {
  encounterId: string; templateKey: string; templateVersion: number; status: 'Draft' | 'Signed'; manual: boolean;
  sections: NoteSections; version: number; updatedAt: string | null; signature?: NoteSignature | null; addenda?: NoteAddendum[];
  /** The name this user's signature will show (the profile's, else the account's); the sign modal repeats it. */
  signAsName?: string | null;
  recordingConsent?: RecordingConsent | null;
  /** Section ids with an AI draft or suggestion the doctor has not accepted or dismissed (written from MDV-P3 on). */
  undecidedAi?: string[];
  /** MDV-P2-042: on a note with nothing saved, the template the server picked from the patient's age and problems (the note starts on it). */
  suggestedTemplateKey?: string | null;
};
/** What an autosave sends besides the version: the template, the whole note (a section left out is cleared) and the recording consent. */
export type NoteDraft = { templateKey: string; sections: NoteSections; recordingConsent: RecordingConsent | null };

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
  const byId = new Map(to.sections.map(s => [s.id, s]));
  const kept: NoteSections = {};
  const dropped: string[] = [];
  for (const [id, v] of Object.entries(sections).filter(([, v]) => !isBlank(v))) {
    const s = byId.get(id);
    // A structured section keeps the answers to items the new template also asks (MDV-P2-042); answers it does not ask are named as dropped.
    const next = s && isStructured(s) ? keepItems(s, v) : { text: s ? v : '', dropped: !s };
    if (next.text) kept[id] = next.text;
    if (next.dropped) dropped.push(id);
  }
  return { kept, dropped };
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

/** Why a sign call was refused, from the status and problem body: what the doctor sees or does next (MDV-P2-012). Unreviewed: a checklist gate the server holds (MDV-P2-044). */
export type SignFailure = { kind: 'reauth' | 'stale' | 'signed' | 'denied' | 'unreviewed' | 'failed' } | { kind: 'blank'; sections: string[] };

export function signFailure(status: number | undefined, body: unknown): SignFailure {
  const b = (typeof body === 'object' && body !== null ? body : {}) as { code?: unknown; errors?: Record<string, string[]> };
  if (status === 403) return { kind: b.code === 'reauth_required' ? 'reauth' : 'denied' };
  if (status === 409) return { kind: b.code === 'stale_note' ? 'stale' : b.code === 'note_signed' ? 'signed' : 'failed' };
  const blank = Object.keys(b.errors ?? {}).filter(k => k.startsWith('sections.')).map(k => k.slice('sections.'.length));
  if (status === 400 && blank.length > 0) return { kind: 'blank', sections: blank };
  return status === 400 && (b.errors?.recordingConsent || b.errors?.undecidedAi) ? { kind: 'unreviewed' } : { kind: 'failed' };
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

/** The server PDF refuses characters none of its fonts has, such as an emoji (422 pdf_unsupported_text): the print view shows them. */
export const pdfFailure = (status: number | undefined): 'unsupported' | 'failed' => (status === 422 ? 'unsupported' : 'failed');

// Verified prefill, quick phrases and earlier-note snippets (MDV-P2-009). Nothing is added unless the doctor presses a button; the server decides what is verified.
/** One verified nurse-intake reading of this visit with its source: who took it and when. `recordedByName` is null when unknown. */
export type VerifiedVital = { code: string; value: number; unit: string; takenAt: string; source: string; recordedByName: string | null; flag: 'High' | 'Low' | 'Changed' | null };
/** A signed note of the same patient, offered as snippets. */
export type PreviousNote = { encounterId: string; signedAt: string; signedByName: string | null; templateKey: string; sections: NoteSections };

/** The structured section ids every template version that has them uses (NoteTemplates.ReviewOfSystems and Examination on the server). */
export const structuredIds = ['reviewOfSystems', 'examination'];
export const phraseKey = (key: string) => `noteTemplates.phrase.${key}` as MessageKey;
export const vitalNameKey = (code: string) => `noteAssist.vital.${code}` as MessageKey;
/** Quick phrases and snippets go to the section last edited, else the plan. */
export const defaultTargetSection = 'plan';

/** The sections of an earlier note that have text, in the current template's order first, then any the template has no place for. */
// Earlier answers to the review of systems and examination are not offered (MDV-P2-042): today's findings are asked and examined today.
export function snippetSections(note: Pick<PreviousNote, 'sections'>, template: NoteTemplate) {
  const ids = Object.keys(note.sections).filter(id => !isBlank(note.sections[id]) && !structuredIds.includes(id));
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
