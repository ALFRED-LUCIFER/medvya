/** Shared fetch for every feature: a timeout and bounded, jittered retry for requests that are safe to repeat (MDV-P1-048). */
export const timeoutMs = 15_000;
export const maxRetries = 2;
const retriableStatus = new Set([502, 503, 504]);
const backoffMs = (attempt: number) => Math.random() * 400 * 2 ** attempt;

/** The request got no answer in time. Carries no URL or body, so nothing patient-related can leak through it. */
export class RequestTimeoutError extends Error {
  constructor() { super('request timed out'); }
}

// Only GETs and POSTs that carry an Idempotency-Key (server dedupe, MDV-P1-047) may be repeated; anything else could act twice.
const safeToRepeat = (init: RequestInit) =>
  ['GET', 'HEAD', undefined].includes(init.method?.toUpperCase()) || new Headers(init.headers).has('Idempotency-Key');

async function attempt(url: string, init: RequestInit): Promise<Response> {
  const timer = new AbortController();
  const id = setTimeout(() => timer.abort(), timeoutMs);
  const signal = init.signal ? AbortSignal.any([init.signal, timer.signal]) : timer.signal;
  try {
    return await fetch(url, { credentials: 'same-origin', ...init, signal });
  } catch (e) {
    if (timer.signal.aborted && !init.signal?.aborted) throw new RequestTimeoutError();
    throw e;
  } finally { clearTimeout(id); }
}

export async function apiFetch(url: string, init: RequestInit = {}): Promise<Response> {
  const retries = safeToRepeat(init) ? maxRetries : 0;
  for (let n = 0; ; n++) {
    try {
      const res = await attempt(url, init);
      if (n >= retries || !retriableStatus.has(res.status)) return res;
    } catch (e) {
      if (n >= retries || init.signal?.aborted) throw e;
    }
    await new Promise((r) => setTimeout(r, backoffMs(n)));
  }
}
