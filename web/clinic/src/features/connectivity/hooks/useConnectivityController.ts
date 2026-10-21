import { useCallback, useEffect, useState } from 'react';
import { isOnlineNow, isReachable, onConnectivityChange } from '../services/connectivity';

export function useConnectivityController() {
  const [online, setOnline] = useState(isOnlineNow);
  useEffect(() => onConnectivityChange(setOnline), []);
  const retry = useCallback(async () => setOnline(await isReachable()), []);
  return { online, retry };
}
