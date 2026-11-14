export function onConnectivityChange(handler: (online: boolean) => void): () => void {
  const up = () => handler(true), down = () => handler(false);
  window.addEventListener('online', up);
  window.addEventListener('offline', down);
  return () => { window.removeEventListener('online', up); window.removeEventListener('offline', down); };
}

export const isOnlineNow = () => navigator.onLine;

export const reloadPage = () => window.location.reload();
