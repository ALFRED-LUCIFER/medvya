import { useCallback, useEffect, useState } from 'react';

/** Browser online state plus a real reachability check against our own origin for "Retry now". */
export default function useOnline() {
  const [online, setOnline] = useState(() => navigator.onLine);
  useEffect(() => {
    const up = () => setOnline(true), down = () => setOnline(false);
    window.addEventListener('online', up);
    window.addEventListener('offline', down);
    return () => { window.removeEventListener('online', up); window.removeEventListener('offline', down); };
  }, []);
  const retry = useCallback(async () => {
    try { await fetch(window.location.origin, { method: 'HEAD', cache: 'no-store' }); setOnline(true); } catch { setOnline(false); }
  }, []);
  return { online, retry };
}
