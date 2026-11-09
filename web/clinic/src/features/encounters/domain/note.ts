import { z } from 'zod';
import type { MessageKey } from '@medvya/i18n';

// The consultation note draft (MDV-P2-009) and its templates (MDV-P2-025): what the server answers. The server decides access, structure and
// versions; these rules only save a round trip and keep the doctor's text safe.
export type NoteSection = { id: string; required: boolean };
export type NoteTemplate = { key: string; version: number; sections: NoteSection[]; quickPhrases: string[] };
export type NoteCatalog = { templates: NoteTemplate[]; mine: string[] };
export type NoteSections = Record<string, string>;
export type NoteView = {
  encounterId: string; templateKey: string; templateVersion: number; status: 'Draft'; manual: boolean;
  sections: NoteSections; version: number; updatedAt: string | null;
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
