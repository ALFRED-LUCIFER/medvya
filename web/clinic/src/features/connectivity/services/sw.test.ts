import swSource from '../../../../public/sw.js?raw';
import { describe, expect, it, vi } from 'vitest';

// Runs the real public/sw.js against fakes of the worker globals.
function load(fetchImpl: (r: Request) => Promise<Response>, source = swSource) {
  const store = new Map<string, Response>();
  const key = (r: Request | string) => (typeof r === 'string' ? new URL(r, 'https://x.test').href : r.url);
  const cache = { match: async (r: Request | string) => store.get(key(r))?.clone(), put: async (r: Request | string, v: Response) => void store.set(key(r), v) };
  const caches = { open: async () => cache, keys: async () => ['old', 'medvya-static-v1'], delete: vi.fn(async () => true) };
  const handlers: Record<string, (e: unknown) => void> = {};
  const self = { location: { origin: 'https://x.test' }, addEventListener: (t: string, h: (e: unknown) => void) => { handlers[t] = h; },
    skipWaiting: vi.fn(), clients: { claim: vi.fn() } };
  new Function('self', 'caches', 'fetch', source)(self, caches, fetchImpl);
  const fire = async (req: Request) => {
    let p: Promise<Response> | undefined;
    handlers.fetch!({ request: req, respondWith: (x: Promise<Response>) => { p = x; } });
    return p && (await p);
  };
  return { store, caches, self, handlers, fire };
}
const req = (path: string, init: { mode?: string; method?: string } = {}) => {
  const r = new Request(`https://x.test${path}`, { method: init.method ?? 'GET' });
  return Object.defineProperty(r, 'mode', { value: init.mode ?? 'cors' }) as Request;
};
const html = () => new Response('<html></html>', { headers: { 'content-type': 'text/html' } });

describe('asset worker', () => {
  it('caches hashed assets and serves them with no network', async () => {
    let online = true;
    const w = load(async () => { if (!online) throw new TypeError('offline'); return new Response('js'); });
    await w.fire(req('/app/assets/index-abc.js'));
    online = false;
    expect(await (await w.fire(req('/app/assets/index-abc.js')))!.text()).toBe('js');
  });

  it('never caches or intercepts API, sign-in or non-GET requests', async () => {
    const w = load(async () => new Response('{"name":"synthetic"}'));
    for (const r of [req('/v1/patients/1'), req('/bff/session'), req('/v1/assets/a.js'), req('/app/assets/a.js', { method: 'POST' })]) {
      expect(await w.fire(r)).toBeUndefined();
    }
    expect(w.store.size).toBe(0);
  });

  it('falls back to the one stored shell for any route when offline, and prefers the network when online', async () => {
    let online = true;
    const w = load(async () => { if (!online) throw new TypeError('offline'); return html(); });
    await w.fire(req('/app/schedule', { mode: 'navigate' }));
    expect([...w.store.keys()]).toEqual(['https://x.test/app/index.html']);
    online = false;
    expect(await (await w.fire(req('/app/patients/p-1', { mode: 'navigate' })))!.text()).toBe('<html></html>');
  });

  it('does not store a non-HTML navigation response', async () => {
    const w = load(async () => new Response('{"patient":"x"}', { headers: { 'content-type': 'application/json' } }));
    await w.fire(req('/app/odd', { mode: 'navigate' }));
    expect(w.store.size).toBe(0);
  });

  it('activates a new version at once and drops other caches', async () => {
    const w = load(async () => new Response(''));
    w.handlers.install!({});
    expect(w.self.skipWaiting).toHaveBeenCalled();
    let p: Promise<unknown> = Promise.resolve();
    w.handlers.activate!({ waitUntil: (x: Promise<unknown>) => { p = x; } });
    await p;
    expect(w.caches.delete).toHaveBeenCalledWith('old');
    expect(w.caches.delete).not.toHaveBeenCalledWith('medvya-static-v1');
    expect(w.self.clients.claim).toHaveBeenCalled();
  });
});
