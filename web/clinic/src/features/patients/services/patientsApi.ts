import type { PatientDetails, SearchRequest, SearchResult } from '../domain/directory';

// Same-origin BFF calls (ADR-004): the session cookie rides along; state-changing calls carry the CSRF header.
const json = { 'Content-Type': 'application/json', 'X-Medvya-Csrf': '1' };

/** A refused API call: the status, plus the first field/code pair for 400s. Never carries what was sent. */
export class ApiError extends Error {
  readonly status: number;
  readonly code: string | undefined;
  constructor(status: number, code?: string) {
    super(`patients api ${status}`);
    this.status = status;
    this.code = code;
  }
}

async function fail(res: Response): Promise<never> {
  const problem = res.status === 400 ? ((await res.json().catch(() => null)) as { errors?: { code?: string }[] } | null) : null;
  throw new ApiError(res.status, problem?.errors?.[0]?.code);
}

/** Directory page or search. The search text travels in the body, never in the URL or query string. */
export async function searchPatients(request: SearchRequest): Promise<SearchResult> {
  const res = await fetch('/v1/patients/search', {
    method: 'POST', credentials: 'same-origin', headers: json, body: JSON.stringify(request),
  });
  if (!res.ok) return fail(res);
  return res.json() as Promise<SearchResult>;
}

/** Opens one record; the server audits the read before returning it. The URL holds only the opaque id. */
export async function fetchPatient(id: string): Promise<PatientDetails> {
  const res = await fetch(`/v1/patients/${encodeURIComponent(id)}`, { credentials: 'same-origin' });
  if (!res.ok) return fail(res);
  return res.json() as Promise<PatientDetails>;
}
