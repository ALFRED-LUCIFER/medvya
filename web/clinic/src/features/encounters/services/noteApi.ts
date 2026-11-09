import { post, send } from '../../schedule/services/scheduleApi';
import type { NoteCatalog, NoteDraft, NoteView } from '../domain/note';

const put = <T>(url: string, body: unknown) =>
  send<T>(url, { method: 'PUT', headers: { 'X-Medvya-Csrf': '1', 'Content-Type': 'application/json' }, body: JSON.stringify(body) });

// Consult note (MDV-P2-009). The note text travels only in the body; ids only in paths.
export const fetchNote = (encounterId: string) => send<NoteView>(`/v1/encounters/${encounterId}/note`);
/** `manual` is true: there is no recording to draft from until MDV-P3, so every note is typed by the doctor. */
export const saveNote = (encounterId: string, draft: NoteDraft, baseVersion: number) =>
  put<NoteView>(`/v1/encounters/${encounterId}/note`, { templateKey: draft.templateKey, manual: true, baseVersion, sections: Object.fromEntries(Object.entries(draft.sections).filter(([, v]) => v.trim())) });

// Note templates (MDV-P2-025).
export const fetchNoteCatalog = () => send<NoteCatalog>('/v1/note-templates');
export const chooseNoteTemplates = (keys: string[]) => put<string[]>('/v1/note-templates/mine', { keys });

/** Signs the version the editor last saw. A 403 reauth_required means: confirm the password and second factor first (`startReauth`), then call again. */
export const signNote = (encounterId: string, baseVersion: number) => post<NoteView>(`/v1/encounters/${encounterId}/note/sign`, { baseVersion });

/** Step-up before signing: the provider asks again, then returns to this consult path (an opaque id, never patient data). */
export async function startReauth() {
  const { location } = await post<{ location: string }>('/bff/reauth', { returnUrl: window.location.pathname });
  window.location.assign(location);
}
