import { useEffect, useState } from 'react';
import { isOnlineNow, onConnectivityChange, reloadPage } from '../services/connectivity';

export function useConnectivityController() {
  const [online, setOnline] = useState(isOnlineNow);
  useEffect(() => onConnectivityChange(setOnline), []);
  return { online, reload: reloadPage };
}
