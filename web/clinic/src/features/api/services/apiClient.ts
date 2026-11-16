/** Shared fetch for every feature: a timeout and bounded, jittered retry for requests that are safe to repeat (MDV-P1-048). */
export const timeoutMs = 15_000;
export const maxRetries = 2;
const retriableStatus = new Set([429, 502, 503, 504]);
const backoffMs = (attempt: number) => Math.random() * 400 * 2 ** attempt;
// A 429 (rate limit, #1365) says when to come back. Nothing was done, so asking again is safe, but only within 5 s of waiting in all: a
// person reads a longer pause as a frozen screen, and the server's windows are a minute, so a longer Retry-After goes straight to the
// screen's error state instead (they can try again themselves).
export const maxRefusedWaitMs = 5_000;
const refusedWaitMs = (res: Response) => {
  const seconds = Number(res.headers.get('Retry-After'));
  return seconds > 0 ? seconds * 1000 : null;
};

// Every wait between tries ends at once when the caller gives up (the screen closed, the query was cancelled).
const sleep = (ms: number, signal: AbortSignal | null | undefined) => new Promise<void>((resolve, reject) => {
  const stop = () => { clearTimeout(id); reject(signal?.reason); };
  const id = setTimeout(() => { signal?.removeEventListener('abort', stop); resolve(); }, ms);
  signal?.addEventListener('abort', stop, { once: true });
  if (signal?.aborted) stop();
});

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

// State-changing calls carry the CSRF header the BFF requires (ADR-004). Exported for the SignalR negotiate, which does not use apiFetch.
export const csrf = { 'X-Medvya-Csrf': '1' };
const withCsrf = <T extends RequestInit>(init: T): T => {
  if (['GET', 'HEAD', undefined].includes(init.method?.toUpperCase())) return init;
  const headers = new Headers(init.headers);
  headers.set('X-Medvya-Csrf', '1');
  return { ...init, headers };
};

/** `timeout` overrides the shared timeout for one call that is known to be slow (the set-up finish). */
export async function apiFetch(url: string, given: RequestInit & { timeout?: number } = {}): Promise<Response> {
  const init = withCsrf(given);
  const retries = safeToRepeat(init) ? maxRetries : 0;
  let refusedBudget = maxRefusedWaitMs;
  for (let n = 0; ; n++) {
    let wait = backoffMs(n);
    try {
      const res = await attempt(url, init);
      if (n >= retries || !retriableStatus.has(res.status)) return res;
      if (res.status === 429) {
        wait = refusedWaitMs(res) ?? wait;
        if (wait > refusedBudget) return res;
        refusedBudget -= wait;
      }
    } catch (e) {
      if (n >= retries || init.signal?.aborted) throw e;
    }
    await sleep(wait, init.signal);
  }
}

/** An API answer other than 2xx. `status` tells 403 from other failures; `code` is the 409 reason (e.g. "being_booked"). */
export class ApiError extends Error {
  readonly status: number;
  readonly code: string | undefined;
  /** The problem-details body, for answers that carry data (e.g. the visits an "actions_required" leave affects). */
  readonly body: unknown;
  constructor(status: number, code?: string, body?: unknown) {
    super(`API ${status}`);
    this.status = status;
    this.code = code;
    this.body = body;
  }
}

export async function send<T>(url: string, init: RequestInit = {}): Promise<T> {
  return answer<T>(await apiFetch(url, { credentials: 'same-origin', ...init }));
}

export async function answer<T>(res: Response): Promise<T> {
  if (!res.ok) {
    const body = (await res.json().catch(() => null)) as { code?: string; errors?: Record<string, string[]> } | null;
    // A rejected field (400 problem details) is reported as "field:code" so a screen can name what was wrong.
    const rejected = Object.entries(body?.errors ?? {}).find(([, codes]) => codes.length > 0);
    throw new ApiError(res.status, body?.code ?? (rejected ? `${rejected[0]}:${rejected[1][0]}` : undefined), body);
  }
  return (res.status === 204 ? undefined : await res.json()) as T;
}

/** With a `key` the call carries an Idempotency-Key, so the shared client may repeat it after a timeout without acting twice. */
export const post = <T>(url: string, body: unknown, key?: string) =>
  send<T>(url, { method: 'POST', headers: { 'Content-Type': 'application/json', ...(key ? { 'Idempotency-Key': key } : {}) }, body: JSON.stringify(body) });
