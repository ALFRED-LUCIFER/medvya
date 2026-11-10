import { z } from 'zod';

// Diagnosis coding and the problem list (MDV-P2-010, E-07). The server validates every code and decides what is stored; this file searches the
// country's code set in the browser (a search term never travels) and shapes the forms.
export type Verification = 'Verified' | 'PatientReported' | 'Provisional';
export type ProblemStatus = 'Active' | 'Resolved';
export const verifications: Verification[] = ['Verified', 'Provisional', 'PatientReported'];
export const statuses: ProblemStatus[] = ['Active', 'Resolved'];

export type DiagnosisCode = { code: string; title: string };
export type CodeSet = { system: string; version: string; codes: DiagnosisCode[] };
export type ProblemRow = {
  id: string; code: string | null; codeSystem: string | null; codeSystemVersion: string | null; description: string; verification: Verification;
  status: ProblemStatus; onsetOn: string | null; source: string; addedHere: boolean; confirmed: boolean; recordedAt: string;
};
export type ProblemList = { encounterId: string; patientId: string; rows: ProblemRow[]; locked: boolean };

/** The label key of a verification state: a problem verified in this consultation reads "Added today · verified" (E-07). */
export const verificationKey = (r: Pick<ProblemRow, 'verification' | 'addedHere'>) =>
  r.verification === 'Verified' && r.addedHere ? 'AddedToday' : r.verification;

const fold = (s: string) => s.toLowerCase().replace(/[.\s]/g, '');

/**
 * Codes matching the query, best first: an exact code, then codes that start with it (with or without the dot, any case), then descriptions
 * holding every word. Blank finds nothing.
 * shortcut: a linear scan, fine for the starter list and still well under 300 ms for a full code set (tested); index it if a set grows past that.
 */
export function searchCodes(codes: DiagnosisCode[], query: string, limit = 8): DiagnosisCode[] {
  const q = fold(query);
  if (q === '') return [];
  const words = query.toLowerCase().split(/\s+/).filter(Boolean);
  const rank = (c: DiagnosisCode) => {
    const code = fold(c.code);
    if (code === q) return 0;
    if (code.startsWith(q)) return 1;
    const title = c.title.toLowerCase();
    return words.every(w => title.includes(w)) ? 2 : -1;
  };
  const hits: { c: DiagnosisCode; r: number }[] = [];
  for (const c of codes) {
    const r = rank(c);
    if (r >= 0) hits.push({ c, r });
  }
  return hits.sort((a, b) => a.r - b.r || a.c.code.localeCompare(b.c.code)).slice(0, limit).map(h => h.c);
}

export const addSchema = z.object({
  description: z.string().trim().max(200),
  verification: z.enum(['Verified', 'PatientReported', 'Provisional']),
  onsetOn: z.string(),
});
export type AddDraft = z.infer<typeof addSchema>;
export const emptyAdd: AddDraft = { description: '', verification: 'Verified', onsetOn: '' };

export const editSchema = z.object({
  verification: z.enum(['Verified', 'PatientReported', 'Provisional']),
  status: z.enum(['Active', 'Resolved']),
  onsetOn: z.string(),
});
export type EditDraft = z.infer<typeof editSchema>;

export type Problem = 'ended' | 'duplicate' | 'unknown' | 'description' | 'future' | 'failed';
/** The server's 409 code or rejected field ("code:unknown") as one of the messages the screen has. */
export function problemOf(code: string | undefined): Problem {
  if (code === 'ended' || code === 'duplicate') return code;
  if (code === 'code:unknown') return 'unknown';
  if (code?.startsWith('description:')) return 'description';
  if (code === 'onsetOn:future') return 'future';
  return 'failed';
}
