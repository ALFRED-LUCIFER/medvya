// The console host keeps the tokens (BFF, ADR-004); the browser sends its HTTP-only cookie and, on writes, the CSRF header.
const csrf = { 'X-Medvya-Csrf': '1' };

// The same timeout and bounded, jittered retry as the clinic's apiFetch (MDV-P1-048), copied because apps cannot import each other.
// Only a GET is repeated: a console write (approve, suspend) must never act twice.
export const timeoutMs = 15_000;
export const maxRetries = 2;
const retriableStatus = new Set([502, 503, 504]);
const backoffMs = (attempt: number) => Math.random() * 400 * 2 ** attempt;

/** The request got no answer in time. Carries no URL or body. */
export class RequestTimeoutError extends Error {
  constructor() { super('request timed out'); }
}

async function attempt(url: string, init: RequestInit): Promise<Response> {
  const timer = new AbortController();
  const id = setTimeout(() => timer.abort(), timeoutMs);
  try {
    return await fetch(url, { credentials: 'same-origin', ...init, signal: timer.signal });
  } catch (e) {
    if (timer.signal.aborted) throw new RequestTimeoutError();
    throw e;
  } finally { clearTimeout(id); }
}

export async function apiFetch(url: string, init: RequestInit = {}): Promise<Response> {
  const retries = (init.method ?? 'GET').toUpperCase() === 'GET' ? maxRetries : 0;
  for (let n = 0; ; n++) {
    try {
      const res = await attempt(url, init);
      if (n >= retries || !retriableStatus.has(res.status)) return res;
    } catch (e) {
      if (n >= retries) throw e;
    }
    await new Promise(r => setTimeout(r, backoffMs(n)));
  }
}

/** A refused request: its status and, for 400, the fields and codes the server rejected (e.g. { reason: ['required'] }). */
export class ApiError extends Error {
  readonly status: number;
  readonly fields: Record<string, string[]>;
  constructor(status: number, fields: Record<string, string[]> = {}) { super(`request failed ${status}`); this.status = status; this.fields = fields; }
}

async function refused(res: Response): Promise<ApiError> {
  try { return new ApiError(res.status, ((await res.json()) as { errors?: Record<string, string[]> }).errors ?? {}); }
  catch { return new ApiError(res.status); }
}

export async function getJson<T>(url: string): Promise<T> {
  const res = await apiFetch(url);
  if (!res.ok) throw await refused(res);
  return res.json() as Promise<T>;
}

export async function post(url: string, body?: unknown): Promise<Response> {
  const res = await apiFetch(url, {
    method: 'POST',
    headers: body === undefined ? csrf : { ...csrf, 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!res.ok) throw await refused(res);
  return res;
}
