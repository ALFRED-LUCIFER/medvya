import { csrf, post, send } from '../../schedule/services/scheduleApi';
import type { CodeSet, ProblemList, ProblemStatus, Verification } from '../domain/problems';

// The problem list (MDV-P2-010). Only ids are in a path; a code, a description and the onset go in bodies, never a URL.
const base = (encounterId: string) => `/v1/encounters/${encounterId}/problems`;
export const fetchCodeSet = () => send<CodeSet>('/v1/diagnosis-codes');
export const fetchProblems = (encounterId: string) => send<ProblemList>(base(encounterId));
export const addProblem = (encounterId: string, p: { code: string | null; description: string | null; verification: Verification; onsetOn: string | null }) =>
  post<ProblemList>(base(encounterId), p);
export const updateProblem = (encounterId: string, problemId: string, p: { verification: Verification; status: ProblemStatus; onsetOn: string | null }) =>
  send<ProblemList>(`${base(encounterId)}/${problemId}`, { method: 'PUT', headers: { ...csrf, 'Content-Type': 'application/json' }, body: JSON.stringify(p) });
