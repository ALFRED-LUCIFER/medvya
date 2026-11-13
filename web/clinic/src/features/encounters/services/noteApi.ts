import { apiFetch } from '../../api/services/apiClient';
import { ApiError, csrf, post, send } from '../../schedule/services/scheduleApi';
import type { AddendumForm, NoteCatalog, NoteDraft, NoteView, PreviousNote, VerifiedVital } from '../domain/note';
import type { IntakeComplaint } from '../domain/complaint';

const put = <T>(url: string, body: unknown, keepalive = false) =>
  send<T>(url, { method: 'PUT', headers: { 'X-Medvya-Csrf': '1', 'Content-Type': 'application/json' }, body: JSON.stringify(body), keepalive });

// Consult note (MDV-P2-009). The note text travels only in the body; ids only in paths.
export const fetchNote = (encounterId: string) => send<NoteView>(`/v1/encounters/${encounterId}/note`);
/** `manual` is true: there is no recording to draft from until MDV-P3, so every note is typed by the doctor. */
/** `keepalive`: sent as the page goes away (MDV-P1-089), so the browser finishes it after the page is gone. */
export const saveNote = (encounterId: string, draft: NoteDraft, baseVersion: number, keepalive = false) =>
  put<NoteView>(`/v1/encounters/${encounterId}/note`, { templateKey: draft.templateKey, manual: true, recordingConsent: draft.recordingConsent, baseVersion, sections: Object.fromEntries(Object.entries(draft.sections).filter(([, v]) => v.trim())) }, keepalive);

// MDV-P1-089: a save sent with keepalive as the page went away can reach the server after the reloaded page has read the note. This tab keeps
// only the encounter id and the version that save was based on (no note text: AGENTS.md 1.2), so the next read waits until the server has it.
const leftKey = (encounterId: string) => `medvya.note.leftAt.${encounterId}`;
export const markLeftWithSave = (encounterId: string, baseVersion: number) => {
  try { sessionStorage.setItem(leftKey(encounterId), String(baseVersion)); } catch { /* storage blocked: the next read does not wait */ }
};
/** The note, read again up to 10 times (3 s) while it is still older than a save this tab sent on leaving. */
export async function fetchNoteAfterLeaving(encounterId: string, waitMs = 300): Promise<NoteView> {
  let left: number | null = null;
  try {
    const v = sessionStorage.getItem(leftKey(encounterId));
    sessionStorage.removeItem(leftKey(encounterId));
    left = v === null ? null : Number(v);
  } catch { /* storage blocked */ }
  let note = await fetchNote(encounterId);
  // shortcut: a keepalive save that failed (offline, stale) is waited for in vain for 3 s, then the server's note shows; upgrade with a server-side unsent-text endpoint if that ever matters.
  for (let i = 0; left !== null && note.version <= left && i < 10; i++) {
    await new Promise(r => setTimeout(r, waitMs));
    note = await fetchNote(encounterId);
  }
  return note;
}

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
