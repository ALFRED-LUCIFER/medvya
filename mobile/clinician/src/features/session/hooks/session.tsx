import { useQueryClient } from '@tanstack/react-query';
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { onSessionEnded } from '../../api/services/api';
import { purgeExpired } from '../../api/services/store';
import { shouldLock, type Session } from '../domain/session';
import { onForeground } from '../services/appState';
import { restoreProfile, saveProfile, signOutDevice, wipeDevice } from '../services/session';

// shortcut: duplicated in mobile/patient; both move to packages/auth-client with native tokens (MDV-P5-033).
type SessionState = {
  /** False until the stored session (if any) is read at launch: nothing is shown before, so sign-in never flashes. */
  ready: boolean;
  session: Session | null;
  locked: boolean;
  biometric: boolean;
  begin: (s: Session, biometric: boolean) => void;
  lock: () => void;
  unlock: () => void;
  signOut: () => void;
};
const SessionContext = createContext<SessionState | null>(null);

/** Who is signed in, and the 15-minute lock. The session is kept in secure-store, so the app reopens locked, not signed out. */
export function SessionProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const [ready, setReady] = useState(false);
  const [session, setSession] = useState<Session | null>(null);
  const [locked, setLocked] = useState(false);
  const [biometric, setBiometric] = useState(false);
  const backgroundedAt = useRef<number | null>(null);

  // Sign-out, Switch user, the failed-PIN limit and a revoke (a refused refresh) all end here: nothing cached outlives the session (ADR-011 §3, §5).
  const signOut = useCallback(() => {
    setSession(null); setLocked(false); setBiometric(false); queryClient.clear(); void signOutDevice();
  }, [queryClient]);
  useEffect(() => onSessionEnded(signOut), [signOut]);

  // A stored session reopens locked: the app cannot know how long it was closed. Without one, anything on the phone is left
  // from a session that ended without a sign-out, so it goes.
  useEffect(() => {
    void (async () => {
      const stored = await restoreProfile().catch(() => null);
      if (stored) { setSession(stored.session); setBiometric(stored.biometric); setLocked(true); }
      else await wipeDevice().catch(() => undefined);
      setReady(true);
    })();
  }, []);

  useEffect(() => onForeground(active => {
    if (!active) { backgroundedAt.current = Date.now(); return; }
    if (shouldLock(backgroundedAt.current, Date.now())) setLocked(true);
    backgroundedAt.current = null;
    void purgeExpired().catch(() => undefined);
  }), []);

  const value = useMemo<SessionState>(() => ({
    ready, session, locked, biometric,
    begin: (s, b) => { setSession(s); setBiometric(b); setLocked(false); void saveProfile(s, b).catch(() => undefined); }, // unsaved, the next launch asks to sign in again
    lock: () => setLocked(true),
    unlock: () => setLocked(false),
    signOut,
  }), [ready, session, locked, biometric, signOut]);
  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}

export function useSession() {
  const s = useContext(SessionContext);
  if (!s) throw new Error('useSession outside SessionProvider');
  return s;
}

/**
 * For screens behind the sign-in guard. On sign-out these screens render once more before the router removes them,
 * so they keep the last session for that render instead of crashing.
 */
export function useSignedIn() {
  const s = useSession();
  const last = useRef(s.session);
  if (s.session) last.current = s.session;
  if (!last.current) throw new Error('useSignedIn outside the signed-in screens');
  return { ...s, session: last.current };
}
