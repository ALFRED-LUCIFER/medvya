import { apiFetch } from '../../api/services/apiClient';
import type { ConsentState } from '../domain/consents';
import type { PatientDetails, RevealedIdentifiers, SearchRequest, SearchResult } from '../domain/directory';

// Same-origin BFF calls (ADR-004): the session cookie rides along; state-changing calls carry the CSRF header.
export const jsonHeaders = { 'Content-Type': 'application/json', 'X-Medvya-Csrf': '1' };

export type FieldError = { field: string; code: string };

/** A refused API call: the status, plus the field/code pairs of a 400. Never carries what was sent. */
export class ApiError extends Error {
  readonly status: number;
  readonly errors: FieldError[];
  constructor(status: number, errors: FieldError[] = []) {
    super(`patients api ${status}`);
    this.status = status;
    this.errors = errors;
  }

  get code(): string | undefined {
    return this.errors[0]?.code;
  }
}

export async function fail(res: Response): Promise<never> {
  const problem = res.status === 400 ? ((await res.json().catch(() => null)) as { errors?: FieldError[] } | null) : null;
  throw new ApiError(res.status, problem?.errors ?? []);
}

/** Directory page or search. The search text travels in the body, never in the URL or query string. */
export async function searchPatients(request: SearchRequest): Promise<SearchResult> {
  const res = await apiFetch('/v1/patients/search', {
    method: 'POST', credentials: 'same-origin', headers: jsonHeaders, body: JSON.stringify(request),
  });
  if (!res.ok) return fail(res);
  return res.json() as Promise<SearchResult>;
}

/** Opens one record; the server audits the read before returning it. The URL holds only the opaque id. */
export async function fetchPatient(id: string): Promise<PatientDetails> {
  const res = await apiFetch(`/v1/patients/${encodeURIComponent(id)}`, { credentials: 'same-origin' });
  if (!res.ok) return fail(res);
  return res.json() as Promise<PatientDetails>;
}

/** The full identifier and member numbers of one record. A POST: each reveal is an audited action, never prefetched or cached. */
export async function revealIdentifiers(id: string): Promise<RevealedIdentifiers> {
  const res = await apiFetch(`/v1/patients/${encodeURIComponent(id)}/identifiers/reveal`, { method: 'POST', credentials: 'same-origin', headers: jsonHeaders });
  if (!res.ok) return fail(res);
  return res.json() as Promise<RevealedIdentifiers>;
}

/** The current decision per consent (newest receipt); the server audits the read. */
export async function fetchConsents(id: string): Promise<ConsentState[]> {
  const res = await apiFetch(`/v1/patients/${encodeURIComponent(id)}/consents`, { credentials: 'same-origin' });
  if (!res.ok) return fail(res);
  return ((await res.json()) as { decisions: ConsentState[] }).decisions;
}

/** A changed decision is a new receipt on the server; the old ones stay as history. */
export async function changeConsent(id: string, body: unknown): Promise<void> {
  const res = await apiFetch(`/v1/patients/${encodeURIComponent(id)}/consents`, { method: 'POST', credentials: 'same-origin', headers: jsonHeaders, body: JSON.stringify(body) });
  if (!res.ok) return fail(res);
}
