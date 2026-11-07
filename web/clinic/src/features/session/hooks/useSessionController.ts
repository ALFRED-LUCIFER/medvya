import { useMemo } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { nextSession, toSession, type SessionStatus } from '../domain/session';
import { chooseFacility, fetchMeContext, hadSession, rememberSession, SessionEndedError, signOut, startSignIn } from '../services/sessionApi';

export const sessionQueryKey = ['me'];
// The server-side idle timeout is at least a minute, so polling each minute shows "Session expired" within a minute of it.
const pollMs = 60_000;

export function useSessionController() {
  const queryClient = useQueryClient();
  const me = useQuery({
    queryKey: sessionQueryKey,
    queryFn: async () => {
      const status = nextSession(hadSession(), await fetchMeContext());
      rememberSession(status.kind !== 'signedOut');
      return status;
    },
    refetchInterval: q => (q.state.data?.kind === 'signedIn' ? pollMs : false),
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
  const session = useMemo(() => (data?.kind === 'signedIn' ? toSession(data.me) : null), [data]);
  return {
    loading: me.isPending,
    expired: data?.kind === 'expired',
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
