import { useMemo } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { toSession } from '../domain/session';
import { fetchMeContext, signInFailed, signOut, startSignIn } from '../services/sessionApi';

export function useSessionController() {
  const queryClient = useQueryClient();
  const me = useQuery({ queryKey: ['me'], queryFn: fetchMeContext });
  const logout = useMutation({ mutationFn: signOut, onSuccess: () => queryClient.setQueryData(['me'], null) });
  const session = useMemo(() => (me.data ? toSession(me.data) : null), [me.data]);
  return {
    loading: me.isPending,
    session,
    displayName: session?.user.displayName ?? null,
    failed: signInFailed(),
    signIn: startSignIn,
    signOut: () => logout.mutate(),
    signingOut: logout.isPending,
  };
}
