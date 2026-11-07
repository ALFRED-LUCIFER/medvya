// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest';
import { fetchMeContext } from './sessionApi';

const me = { user: { id: 'u-1', displayName: 'A. Joseph' } };
const answer = (status: number) => new Response(JSON.stringify(me), { status });

afterEach(() => { vi.unstubAllGlobals(); window.medvyaMeContext = undefined; });

// MDV-P1-037: public/prefetch.js starts the first read while the bundle downloads.
describe('fetchMeContext', () => {
  it('takes the read prefetch.js started, once, then reads afresh', async () => {
    const f = vi.fn().mockResolvedValue(answer(200));
    vi.stubGlobal('fetch', f);
    window.medvyaMeContext = Promise.resolve(answer(200));
    expect(await fetchMeContext()).toEqual(me);
    expect(f).not.toHaveBeenCalled();
    expect(await fetchMeContext()).toEqual(me);
    expect(f).toHaveBeenCalledTimes(1);
  });

  it('keeps a prefetched 401 as signed out', async () => {
    vi.stubGlobal('fetch', vi.fn());
    window.medvyaMeContext = Promise.resolve(answer(401));
    expect(await fetchMeContext()).toBeNull();
    expect(fetch).not.toHaveBeenCalled();
  });

  it.each([['failed', undefined], ['answered 503', answer(503)]])('reads again when the prefetch %s', async (_, early) => {
    const f = vi.fn().mockResolvedValue(answer(200));
    vi.stubGlobal('fetch', f);
    window.medvyaMeContext = Promise.resolve(early);
    expect(await fetchMeContext()).toEqual(me);
    expect(f).toHaveBeenCalledTimes(1);
  });
});
