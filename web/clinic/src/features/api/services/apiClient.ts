/** Shared fetch for every feature: a timeout and bounded, jittered retry for requests that are safe to repeat (MDV-P1-048). */
export const timeoutMs = 15_000;
export const maxRetries = 2;
const retriableStatus = new Set([429, 502, 503, 504]);
const backoffMs = (attempt: number) => Math.random() * 400 * 2 ** attempt;
// A 429 (rate limit, #1365) says when to come back: wait that long, at most the server's one-minute window. Nothing was done, so asking again is safe.
const refusedWaitMs = (res: Response) => {
  const seconds = Number(res.headers.get('Retry-After'));
  return seconds > 0 ? Math.min(seconds, 60) * 1000 : null;
};

/** The request got no answer in time. Carries no URL or body, so nothing patient-related can leak through it. */
export class RequestTimeoutError extends Error {
  constructor() { super('request timed out'); }
}

// Only GETs and POSTs that carry an Idempotency-Key (server dedupe, MDV-P1-047) may be repeated; anything else could act twice.
const safeToRepeat = (init: RequestInit) =>
  ['GET', 'HEAD', undefined].includes(init.method?.toUpperCase()) || new Headers(init.headers).has('Idempotency-Key');

// A request made while nobody has touched the page for a minute is a background poll (the bell, the board, a live-update refetch), so it
// is sent as passive: the server does not count it as activity and an unattended screen reaches its idle time-out (#1365). One rule here
// covers every timed poll, because a query function cannot tell a timed refetch from a person opening the screen.
export const activeWindowMs = 60_000;
let lastInput = Date.now(); // loading the page is the person's doing
if (typeof window !== 'undefined')
  for (const type of ['pointerdown', 'keydown', 'wheel'])
    window.addEventListener(type, () => { lastInput = Date.now(); }, { capture: true, passive: true });
const passive = (init: RequestInit): RequestInit => {
  if (Date.now() - lastInput < activeWindowMs) return init;
  const headers = new Headers(init.headers);
  headers.set('X-Medvya-Passive', '1');
  return { ...init, headers };
};

async function attempt(url: string, { timeout = timeoutMs, ...rest }: RequestInit & { timeout?: number }): Promise<Response> {
  const init = passive(rest);
  const timer = new AbortController();
  const id = setTimeout(() => timer.abort(), timeout);
  const signal = init.signal ? AbortSignal.any([init.signal, timer.signal]) : timer.signal;
  try {
    return await fetch(url, { credentials: 'same-origin', ...init, signal });
  } catch (e) {
    if (timer.signal.aborted && !init.signal?.aborted) throw new RequestTimeoutError();
    throw e;
  } finally { clearTimeout(id); }
}

/** `timeout` overrides the shared timeout for one call that is known to be slow (the set-up finish). */
export async function apiFetch(url: string, init: RequestInit & { timeout?: number } = {}): Promise<Response> {
  const retries = safeToRepeat(init) ? maxRetries : 0;
  for (let n = 0; ; n++) {
    let wait = backoffMs(n);
    try {
      const res = await attempt(url, init);
      if (n >= retries || !retriableStatus.has(res.status)) return res;
      if (res.status === 429) wait = refusedWaitMs(res) ?? wait;
    } catch (e) {
      if (n >= retries || init.signal?.aborted) throw e;
    }
    await new Promise((r) => setTimeout(r, wait));
  }
}
