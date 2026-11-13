import { apiFetch } from '../../api/services/apiClient';
import { ApiError, csrf, post, send } from '../../schedule/services/scheduleApi';
import type { AddendumForm, NoteCatalog, NoteDraft, NoteView, PreviousNote, VerifiedVital } from '../domain/note';
import type { IntakeComplaint } from '../domain/complaint';

const put = <T>(url: string, body: unknown) =>
  send<T>(url, { method: 'PUT', headers: { 'X-Medvya-Csrf': '1', 'Content-Type': 'application/json' }, body: JSON.stringify(body) });

// Consult note (MDV-P2-009). The note text travels only in the body; ids only in paths.
export const fetchNote = (encounterId: string) => send<NoteView>(`/v1/encounters/${encounterId}/note`);
/** `manual` is true: there is no recording to draft from until MDV-P3, so every note is typed by the doctor. */
export const saveNote = (encounterId: string, draft: NoteDraft, baseVersion: number) =>
  put<NoteView>(`/v1/encounters/${encounterId}/note`, { templateKey: draft.templateKey, manual: true, recordingConsent: draft.recordingConsent, baseVersion, sections: Object.fromEntries(Object.entries(draft.sections).filter(([, v]) => v.trim())) });

// What the doctor may copy into the note (MDV-P2-009): verified intake vitals of this visit (and the nurse's chief complaint, MDV-P1-104), and earlier signed notes of the patient. Each read is audited.
export const fetchPrefill = (encounterId: string) => send<{ vitals: VerifiedVital[]; chiefComplaint: IntakeComplaint | null }>(`/v1/encounters/${encounterId}/note/prefill`);
export const fetchPrevious = (encounterId: string) => send<{ notes: PreviousNote[] }>(`/v1/encounters/${encounterId}/note/previous`);

// Note templates (MDV-P2-025).
export const fetchNoteCatalog = () => send<NoteCatalog>('/v1/note-templates');
export const chooseNoteTemplates = (keys: string[]) => put<string[]>('/v1/note-templates/mine', { keys });

/** Signs the version the editor last saw. A 403 reauth_required means: confirm the password and second factor first (`startReauth`), then call again. */
export const signNote = (encounterId: string, baseVersion: number) => post<NoteView>(`/v1/encounters/${encounterId}/note/sign`, { baseVersion });

/** Signs an addendum to a signed note (MDV-P2-013): the same re-authentication as signing. The text travels in the body only. */
export const addAddendum = (encounterId: string, addendum: AddendumForm) => post<NoteView>(`/v1/encounters/${encounterId}/note/addenda`, addendum);

/** The server renders and keeps the PDF (POST: each copy handed out is stored and audited); the browser saves it under a name with no patient data. */
export async function downloadNotePdf(encounterId: string): Promise<void> {
  const res = await apiFetch(`/v1/encounters/${encounterId}/note/pdf`, { method: 'POST', credentials: 'same-origin', headers: csrf });
  if (!res.ok) throw new ApiError(res.status);
  const url = URL.createObjectURL(await res.blob());
  const link = document.createElement('a');
  link.href = url;
  link.download = 'consultation-note.pdf';
  link.click();
  URL.revokeObjectURL(url);
}

export const printPage = () => window.print();

/** Step-up before signing: the provider asks again, then returns to this consult path (an opaque id, never patient data). */
export async function startReauth() {
  const { location } = await post<{ location: string }>('/bff/reauth', { returnUrl: window.location.pathname });
  window.location.assign(location);
}
