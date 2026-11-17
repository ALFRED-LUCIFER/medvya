import { afterEach, describe, expect, it, vi } from 'vitest';
import { apiFetch, post, RequestTimeoutError, timeoutMs } from './api';

afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); });

const answer = (status: number) => new Response('{}', { status });

describe('console apiFetch', () => {
  it('retries a read twice on a 503, then gives the last answer', async () => {
    const f = vi.fn().mockImplementation(() => Promise.resolve(answer(503)));
    vi.stubGlobal('fetch', f);
    expect((await apiFetch('/v1/me')).status).toBe(503);
    expect(f).toHaveBeenCalledTimes(3);
  });

  it('never repeats a write', async () => {
    const f = vi.fn().mockImplementation(() => Promise.resolve(answer(503)));
    vi.stubGlobal('fetch', f);
    await expect(post('/v1/organisations/t-1/approve', { reason: 'ok' })).rejects.toMatchObject({ status: 503 });
    expect(f).toHaveBeenCalledTimes(1);
  });

  it('gives up on a request that gets no answer in time', async () => {
    vi.useFakeTimers();
    vi.stubGlobal('fetch', vi.fn((_: string, init: RequestInit) => new Promise((_r, reject) => init.signal!.addEventListener('abort', () => reject(new Error('aborted'))))));
    const call = apiFetch('/v1/organisations/t-1', { method: 'POST' });
    const settled = expect(call).rejects.toBeInstanceOf(RequestTimeoutError);
    await vi.advanceTimersByTimeAsync(timeoutMs);
    await settled;
  });
});
