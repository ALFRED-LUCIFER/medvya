import { useQueryClient } from '@tanstack/react-query';
import { createContext, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { shouldLock, type Session } from '../domain/session';
import { onForeground } from '../services/appState';
import { wipeDevice } from '../services/session';

// shortcut: duplicated in mobile/patient; both move to packages/auth-client with native tokens (MDV-P5-033).
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
 * Who is signed in, and the 15-minute lock.
 * shortcut: the session lives in memory, so closing the app signs out; tokens in expo-secure-store come with MDV-P5-033.
 */
export function SessionProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const [session, setSession] = useState<Session | null>(null);
  const [locked, setLocked] = useState(false);
  const [biometric, setBiometric] = useState(false);
  const backgroundedAt = useRef<number | null>(null);

  // The session lives in memory, so anything on the phone at launch is left from a session that ended without a sign-out.
  // shortcut: once MDV-P5-033 keeps the session in secure-store, wipe here only when no session is stored.
  useEffect(() => { void wipeDevice(); }, []);

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
    // Sign-out, Switch user and the failed-PIN limit all end here: nothing cached outlives the session (ADR-011 §3, §5).
    // shortcut: a revoke (the first 401 from a refresh) has no signal until native tokens (MDV-P5-033); that path must call signOut.
    signOut: () => { setSession(null); setLocked(false); setBiometric(false); queryClient.clear(); void wipeDevice(); },
  }), [session, locked, biometric, queryClient]);
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
