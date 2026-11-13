// @vitest-environment jsdom
import { afterEach, expect, it, vi } from 'vitest';
import { endDevice, fetchDevices } from './devicesApi';

afterEach(() => vi.unstubAllGlobals());

it('ends a session with DELETE, the CSRF header and only the id in the URL', async () => {
  const f = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
  vi.stubGlobal('fetch', f);
  await endDevice('s-1');
  const [url, init] = f.mock.calls[0] as [string, RequestInit];
  expect(url).toContain('/v1/me/sessions/s-1');
  expect(init.method).toBe('DELETE');
  expect(init.headers).toMatchObject({ 'X-Medvya-Csrf': '1' });
});

it('reads the list', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify([{ id: 's-1' }]), { status: 200 })));
  expect(await fetchDevices()).toEqual([{ id: 's-1' }]);
});
