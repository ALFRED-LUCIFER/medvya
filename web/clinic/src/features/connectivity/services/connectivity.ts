/** Real reachability of our own origin; navigator.onLine alone can be wrong. */
export async function isReachable(): Promise<boolean> {
  try { await fetch(window.location.origin, { method: 'HEAD', cache: 'no-store' }); return true; } catch { return false; }
}

export function onConnectivityChange(handler: (online: boolean) => void): () => void {
  const up = () => handler(true), down = () => handler(false);
  window.addEventListener('online', up);
  window.addEventListener('offline', down);
  return () => { window.removeEventListener('online', up); window.removeEventListener('offline', down); };
}

export const isOnlineNow = () => navigator.onLine;
