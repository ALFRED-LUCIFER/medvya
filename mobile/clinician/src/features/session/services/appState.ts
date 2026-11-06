import { AppState } from 'react-native';

/** Calls back when the app goes to the background (false) or comes back (true). Returns the unsubscribe. */
export function onForeground(cb: (active: boolean) => void) {
  const sub = AppState.addEventListener('change', s => { if (s === 'active' || s === 'background') cb(s === 'active'); });
  return () => sub.remove();
}
