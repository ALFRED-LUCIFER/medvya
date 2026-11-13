// MDV-P1-104: the nurse's chief complaint in the doctor's note, and the mark that says where the note's complaint came from.

export const complaintSection = 'chiefComplaint';

/** Where the saved chief complaint came from, as the server decided it (it never takes the editor's word). Null: the doctor's own text. */
export type ComplaintSource = { source: 'NurseIntake' | 'EditedByDoctor'; byName: string | null; at: string | null };

/** The chief complaint the nurse saved at this visit's intake. Null: none (nurse intake off, or nothing saved). */
export type IntakeComplaint = { text: string; byName: string | null; at: string | null };

export type ComplaintMark = { kind: 'nurse' | 'nurseEdited'; byName: string | null; at: string | null };

/** The text to put into Chief complaint: the nurse's, only into a blank field. Text the doctor typed is never replaced; no complaint, nothing guessed. */
export const complaintToPrefill = (current: string | undefined, offered: IntakeComplaint | null | undefined): string | null =>
  !current?.trim() && offered?.text.trim() ? offered.text : null;

/**
 * The mark beside Chief complaint while the doctor types. `saved` is the server's source for `savedText`; before that first save, `prefilled`
 * is the nurse text just put in. From nurse intake while the text is unchanged; edited by the doctor once it differs (or the server says so).
 * Blank text, or the doctor's own, has no mark.
 */
export function complaintMark(text: string | undefined, saved: { source: ComplaintSource | null; text: string | undefined } | null, prefilled: IntakeComplaint | null): ComplaintMark | null {
  if (!text?.trim()) return null;
  const [origin, base] = saved?.source ? [saved.source, saved.text] : prefilled ? [{ source: 'NurseIntake' as const, ...prefilled }, prefilled.text] : [null, undefined];
  if (!origin) return null;
  return { kind: origin.source === 'EditedByDoctor' || text !== base ? 'nurseEdited' : 'nurse', byName: origin.byName, at: origin.at };
}
