import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { apiFetch, maxRetries, RequestTimeoutError, timeoutMs } from './apiClient';

const answer = (status: number) => new Response('{}', { status });
const never = (_: string, init: RequestInit) => new Promise<Response>((_res, rej) => init.signal!.addEventListener('abort', () => rej(new DOMException('x', 'AbortError'))));

beforeEach(() => { vi.useFakeTimers(); vi.spyOn(Math, 'random').mockReturnValue(0.5); });
afterEach(() => { vi.useRealTimers(); vi.unstubAllGlobals(); });

const run = async (p: Promise<unknown>) => { const r = p.then((v) => v, (e: unknown) => e); await vi.runAllTimersAsync(); return r; };

describe('apiFetch', () => {
  it('retries a GET after a network failure and a 503, then returns the answer', async () => {
    const f = vi.fn().mockRejectedValueOnce(new TypeError('net')).mockResolvedValueOnce(answer(503)).mockResolvedValueOnce(answer(200));
    vi.stubGlobal('fetch', f);
    expect(((await run(apiFetch('/v1/x'))) as Response).status).toBe(200);
    expect(f).toHaveBeenCalledTimes(3);
  });

  it('gives up after the bounded number of retries', async () => {
    const f = vi.fn().mockResolvedValue(answer(503));
    vi.stubGlobal('fetch', f);
    expect(((await run(apiFetch('/v1/x'))) as Response).status).toBe(503);
    expect(f).toHaveBeenCalledTimes(maxRetries + 1);
  });

  it('backs off with growing, jittered delays', async () => {
    const f = vi.fn().mockResolvedValue(answer(503));
    vi.stubGlobal('fetch', f);
    const p = apiFetch('/v1/x');
    await vi.advanceTimersByTimeAsync(199); // first wait: 0.5 * 400
    expect(f).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(1);
    expect(f).toHaveBeenCalledTimes(2);
    await vi.advanceTimersByTimeAsync(399); // second wait: 0.5 * 800
    expect(f).toHaveBeenCalledTimes(2);
    await vi.advanceTimersByTimeAsync(1);
    expect(f).toHaveBeenCalledTimes(3);
    await p;
  });

  it('never retries a POST without an Idempotency-Key, on failure or on 503', async () => {
    const f = vi.fn().mockRejectedValue(new TypeError('net'));
    vi.stubGlobal('fetch', f);
    expect(await run(apiFetch('/v1/appointments', { method: 'POST', body: '{}' }))).toBeInstanceOf(TypeError);
    f.mockResolvedValue(answer(503));
    await run(apiFetch('/v1/appointments', { method: 'POST', body: '{}' }));
    expect(f).toHaveBeenCalledTimes(2);
  });

  it('retries a POST that carries an Idempotency-Key, reusing the same key', async () => {
    const f = vi.fn().mockResolvedValueOnce(answer(503)).mockResolvedValueOnce(answer(200));
    vi.stubGlobal('fetch', f);
    await run(apiFetch('/v1/appointments', { method: 'POST', headers: { 'Idempotency-Key': 'k1' } }));
    expect(f.mock.calls.map((c) => (c[1].headers as Record<string, string>)['Idempotency-Key'])).toEqual(['k1', 'k1']);
  });

  it('does not retry a 4xx answer', async () => {
    const f = vi.fn().mockResolvedValue(answer(409));
    vi.stubGlobal('fetch', f);
    await run(apiFetch('/v1/x'));
    expect(f).toHaveBeenCalledTimes(1);
  });

  it('times out after 15 seconds with an error that names nothing', async () => {
    vi.stubGlobal('fetch', vi.fn(never));
    const e = await run(apiFetch('/v1/patients/abc', { method: 'POST' }));
    expect(e).toBeInstanceOf(RequestTimeoutError);
    expect((e as Error).message).not.toContain('abc');
    expect(timeoutMs).toBe(15_000);
  });

  it('waits longer for a call that names its own timeout, and does not send the option to fetch', async () => {
    const f = vi.fn(never);
    vi.stubGlobal('fetch', f);
    const p = apiFetch('/v1/onboarding/finish', { method: 'POST', timeout: 60_000 }).then((v) => v, (e: unknown) => e);
    await vi.advanceTimersByTimeAsync(timeoutMs + 1);
    expect(f.mock.calls[0]![1].signal!.aborted).toBe(false);
    expect(f.mock.calls[0]![1]).not.toHaveProperty('timeout');
    await vi.advanceTimersByTimeAsync(60_000);
    expect(await p).toBeInstanceOf(RequestTimeoutError);
  });

  it('does not retry when the caller aborts', async () => {
    const f = vi.fn(never);
    vi.stubGlobal('fetch', f);
    const c = new AbortController();
    const p = run(apiFetch('/v1/x', { signal: c.signal }));
    c.abort();
    await p;
    expect(f).toHaveBeenCalledTimes(1);
  });
});
