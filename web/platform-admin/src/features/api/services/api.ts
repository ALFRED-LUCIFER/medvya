// The console host keeps the tokens (BFF, ADR-004); the browser sends its HTTP-only cookie and, on writes, the CSRF header.
// shortcut: no timeout or retry like the clinic's apiFetch (MDV-P1-048); a console write must never repeat, and reads are few. Add a
// timeout if console screens poll.
const csrf = { 'X-Medvya-Csrf': '1' };

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
  const res = await fetch(url, { credentials: 'same-origin' });
  if (!res.ok) throw await refused(res);
  return res.json() as Promise<T>;
}

export async function post(url: string, body?: unknown): Promise<Response> {
  const res = await fetch(url, {
    method: 'POST', credentials: 'same-origin',
    headers: body === undefined ? csrf : { ...csrf, 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!res.ok) throw await refused(res);
  return res;
}
