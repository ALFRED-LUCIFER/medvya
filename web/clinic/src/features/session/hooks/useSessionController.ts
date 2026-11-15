import { useEffect, useMemo } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { endingSoon, nextPollMs, nextSession, toSession, type SessionStatus } from '../domain/session';
import { chooseFacility, fetchMeContext, hadSession, rememberSession, SessionEndedError, signOut, startSignIn } from '../services/sessionApi';

export const sessionQueryKey = ['me'];

export function useSessionController() {
  const queryClient = useQueryClient();
  const me = useQuery({
    queryKey: sessionQueryKey,
    queryFn: async () => {
      const status = nextSession(hadSession(), await fetchMeContext(), queryClient.getQueryData<SessionStatus>(sessionQueryKey));
      rememberSession(status.kind !== 'signedOut');
      return status;
    },
    // Each minute, and sooner around the session's end (nextPollMs): "signed out" shows within seconds of it, and the notice before it.
    refetchInterval: q => (q.state.data?.kind === 'signedIn' ? nextPollMs(q.state.data.end) : false),
  });
  // "Stay signed in" (#1365 F-057): one read that counts as activity; its answer carries the new end, or that the session is already over.
  const stay = useMutation({
    mutationFn: () => fetchMeContext(false),
    onSuccess: me => queryClient.setQueryData<SessionStatus>(sessionQueryKey, nextSession(hadSession(), me)),
  });
  const logout = useMutation({
    mutationFn: signOut,
    onSuccess: () => { rememberSession(false); queryClient.setQueryData<SessionStatus>(sessionQueryKey, { kind: 'signedOut' }); },
  });
  const choose = useMutation({
    mutationFn: chooseFacility,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: sessionQueryKey }),
    onError: e => { if (e instanceof SessionEndedError) queryClient.setQueryData<SessionStatus>(sessionQueryKey, { kind: 'expired' }); },
  });
  const data = me.data;
  // Keyed on the context alone: the end time beside it changes with every poll and must not rebuild the session.
  const context = data?.kind === 'signedIn' ? data.me : null;
  const session = useMemo(() => (context ? toSession(context) : null), [context]);
  const end = data?.kind === 'signedIn' ? data.end : null;
  // One clinic means nothing to choose (UX-08): scope the session to it straight away. A failure shows the picker with its error and a retry.
  const only = session && !session.active && session.memberships.length === 1 ? session.memberships[0]!.id : null;
  const autoChoose = only !== null && choose.isIdle;
  useEffect(() => { if (autoChoose) choose.mutate(only); }, [autoChoose, only, choose]);
  return {
    loading: me.isPending,
    /** True while the one membership is being chosen for the user (no picker to show). */
    choosingOnly: only !== null && !choose.isError,
    expired: data?.kind === 'expired',
    /** The session ended because the app was not used (the notice was showing when it did). */
    expiredIdle: data?.kind === 'expired' && data.idle === true,
    /** Under two minutes are left (#1365 F-057): null otherwise; `canStay` is false when the 8-hour limit, not idleness, ends it. */
    ending: end && endingSoon(end) ? { canStay: end.extendable, staying: stay.isPending, failed: stay.isError, onStay: () => stay.mutate() } : null,
    session,
    displayName: session?.user.displayName ?? null,
    signIn: () => startSignIn(),
    /** "Not you?" on the signed-out screen: forget this tab's ended session and show the sign-in start, where another email can be typed. */
    forgetEndedSession: () => { rememberSession(false); queryClient.setQueryData<SessionStatus>(sessionQueryKey, { kind: 'signedOut' }); },
    /** Ends the session; onDone runs once the app shows signed out (e.g. go to the sign-in start). */
    signOut: (onDone?: () => void) => logout.mutate(undefined, { onSuccess: onDone }),
    signingOut: logout.isPending,
    choosingId: choose.isPending ? choose.variables : null,
    chooseFailed: choose.isError && !(choose.error instanceof SessionEndedError),
    /** Scopes the session, then runs onDone (e.g. go to the start screen) once the new context is loaded. */
    chooseFacility: (membershipId: string, onDone: () => void) => choose.mutate(membershipId, { onSuccess: onDone }),
  };
}
