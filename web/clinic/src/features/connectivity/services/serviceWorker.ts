// Registers the static-asset worker (public/sw.js, scope /app/). Build with VITE_DISABLE_SW=true to switch it off: the
// build then removes any worker already installed in browsers. The dev server (base /) never registers one.
export async function registerAssetWorker(): Promise<void> {
  if (!('serviceWorker' in navigator)) return;
  try {
    if (import.meta.env.PROD && import.meta.env.VITE_DISABLE_SW !== 'true') {
      await navigator.serviceWorker.register(`${import.meta.env.BASE_URL}sw.js`);
    } else {
      for (const r of await navigator.serviceWorker.getRegistrations()) await r.unregister();
    }
  } catch { /* the app works without the worker */ }
}
