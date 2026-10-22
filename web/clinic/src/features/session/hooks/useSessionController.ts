import { useMemo } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { nextSession, toSession, type SessionStatus } from '../domain/session';
import { chooseFacility, fetchMeContext, hadSession, rememberSession, SessionEndedError, signInFailed, signOut, startSignIn } from '../services/sessionApi';

const key = ['me'];
// The server-side idle timeout is at least a minute, so polling each minute shows "Session expired" within a minute of it.
const pollMs = 60_000;

export function useSessionController() {
  const queryClient = useQueryClient();
  const me = useQuery({
    queryKey: key,
    queryFn: async () => {
      const status = nextSession(hadSession(), await fetchMeContext());
      rememberSession(status.kind !== 'signedOut');
      return status;
    },
    refetchInterval: q => (q.state.data?.kind === 'signedIn' ? pollMs : false),
  });
  const logout = useMutation({
    mutationFn: signOut,
    onSuccess: () => { rememberSession(false); queryClient.setQueryData<SessionStatus>(key, { kind: 'signedOut' }); },
  });
  const choose = useMutation({
    mutationFn: chooseFacility,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: key }),
    onError: e => { if (e instanceof SessionEndedError) queryClient.setQueryData<SessionStatus>(key, { kind: 'expired' }); },
  });
  const data = me.data;
  const session = useMemo(() => (data?.kind === 'signedIn' ? toSession(data.me) : null), [data]);
  return {
    loading: me.isPending,
    expired: data?.kind === 'expired',
    session,
    displayName: session?.user.displayName ?? null,
    failed: signInFailed(),
    signIn: startSignIn,
    signOut: () => logout.mutate(),
    signingOut: logout.isPending,
    choosingId: choose.isPending ? choose.variables : null,
    chooseFailed: choose.isError && !(choose.error instanceof SessionEndedError),
    /** Scopes the session, then runs onDone (e.g. go to the start screen) once the new context is loaded. */
    chooseFacility: (membershipId: string, onDone: () => void) => choose.mutate(membershipId, { onSuccess: onDone }),
  };
}
