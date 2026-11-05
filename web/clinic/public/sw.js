// MDV-P1-049: static assets only. Caches the hashed build files (/app/assets/*) and the public app shell page; nothing else.
// Never an API response, never anything under /v1/ or /bff/: those are outside the /app/ scope and refused again below.
// Update decision: skipWaiting + clients.claim is safe because the asset cache only ever adds hashed files (an open old page keeps
// its chunks) and the shell is network-first (an online load always gets the newest shell). A changed sw.js activates at once.
// shortcut: superseded hashed files stay in the cache until CACHE is bumped, fine at pilot size; prune on activate if it grows.
const CACHE = 'medvya-static-v1';
const SHELL = '/app/index.html';

self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', e => e.waitUntil(
  caches.keys().then(keys => Promise.all(keys.filter(k => k !== CACHE).map(k => caches.delete(k)))).then(() => self.clients.claim()),
));

self.addEventListener('fetch', e => {
  const { request } = e;
  const url = new URL(request.url);
  // The guard: same-origin GETs under /app/ only. The API lives at /v1/, so it can never match.
  if (request.method !== 'GET' || url.origin !== self.location.origin || !url.pathname.startsWith('/app/')) return;
  if (url.pathname.startsWith('/app/assets/')) e.respondWith(cacheFirst(request));
  else if (request.mode === 'navigate') e.respondWith(shell(request));
});

async function cacheFirst(request) {
  const cache = await caches.open(CACHE);
  const hit = await cache.match(request);
  if (hit) return hit;
  const res = await fetch(request);
  if (res.ok) await cache.put(request, res.clone());
  return res;
}

// Every client route reloads the same public shell, so it is stored under one key, never per URL.
async function shell(request) {
  const cache = await caches.open(CACHE);
  try {
    const res = await fetch(request);
    if (res.ok && (res.headers.get('content-type') ?? '').includes('text/html')) await cache.put(SHELL, res.clone());
    return res;
  } catch (err) {
    const hit = await cache.match(SHELL);
    if (hit) return hit;
    throw err;
  }
}
