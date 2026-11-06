import { AppState } from 'react-native';
// shortcut: duplicated in mobile/clinician; both move to packages/auth-client with native tokens (MDV-P5-033).

/** Calls back when the app goes to the background (false) or comes back (true). Returns the unsubscribe. */
export function onForeground(cb: (active: boolean) => void) {
  const sub = AppState.addEventListener('change', s => { if (s === 'active' || s === 'background') cb(s === 'active'); });
  return () => sub.remove();
}
