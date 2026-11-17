import { useQueryClient } from '@tanstack/react-query';
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { onSessionEnded } from '../../api/services/api';
import { shouldLock, type Session } from '../domain/session';
import { onForeground } from '../services/appState';
import { endLeftoverSession, signOutDevice } from '../services/session';

// shortcut: duplicated in mobile/clinician; both move to packages/auth-client with native tokens (MDV-P5-033).
type SessionState = {
  session: Session | null;
  locked: boolean;
  biometric: boolean;
  begin: (s: Session, biometric: boolean) => void;
  lock: () => void;
  unlock: () => void;
  signOut: () => void;
};
const SessionContext = createContext<SessionState | null>(null);

/**
 * Who is signed in, and the 15-minute lock. With a server the refresh token is in secure-store (api.ts), but the session
 * lives in memory, so closing the app signs out (services/session.ts endLeftoverSession).
 */
export function SessionProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const [session, setSession] = useState<Session | null>(null);
  const [locked, setLocked] = useState(false);
  const [biometric, setBiometric] = useState(false);
  const backgroundedAt = useRef<number | null>(null);

  // Sign-out and a revoke (a refused refresh) both end here: nothing cached or stored outlives the session (ADR-011 §3).
  const signOut = useCallback(() => {
    setSession(null); setLocked(false); setBiometric(false); queryClient.clear(); void signOutDevice().catch(() => undefined);
  }, [queryClient]);
  useEffect(() => onSessionEnded(signOut), [signOut]);
  useEffect(() => { void endLeftoverSession().catch(() => undefined); }, []);

  useEffect(() => onForeground(active => {
    if (!active) { backgroundedAt.current = Date.now(); return; }
    if (shouldLock(backgroundedAt.current, Date.now())) setLocked(true);
    backgroundedAt.current = null;
  }), []);

  const value = useMemo<SessionState>(() => ({
    session, locked, biometric,
    begin: (s, b) => { setSession(s); setBiometric(b); setLocked(false); },
    lock: () => setLocked(true),
    unlock: () => setLocked(false),
    signOut,
  }), [session, locked, biometric, signOut]);
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
  const [last, setLast] = useState(s.session);
  if (s.session && s.session !== last) setLast(s.session); // derived state, set during render (React docs) instead of a ref read in render
  const session = s.session ?? last;
  if (!session) throw new Error('useSignedIn outside the signed-in screens');
  return { ...s, session };
}
