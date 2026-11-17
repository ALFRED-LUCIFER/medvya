import { afterEach, describe, expect, it, vi } from 'vitest';
import { newDisplayLink } from './queueTvApi';

afterEach(() => vi.unstubAllGlobals());

describe('queue display api', () => {
  it('asks for a new link with the CSRF header and only the display id in the URL (#1413)', async () => {
    const created = { display: { id: 'q-1', facilityId: 'f-1', name: 'Lobby TV', createdAt: '2026-10-01T04:00:00Z', revokedAt: null }, token: 'synthetic' };
    const fetch = vi.fn(async (_url: string, _init?: RequestInit) => Response.json(created));
    vi.stubGlobal('fetch', fetch);
    expect(await newDisplayLink('q-1')).toEqual(created);
    const [url, init] = fetch.mock.calls[0]!;
    expect(url).toBe('/v1/queue/displays/q-1/link');
    expect(init?.method).toBe('POST');
    expect(new Headers(init!.headers).get('X-Medvya-Csrf')).toBe('1'); // apiFetch adds it to every write
  });
});
