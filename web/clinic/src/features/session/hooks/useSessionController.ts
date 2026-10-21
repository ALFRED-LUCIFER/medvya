import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { fetchMeContext, signInFailed, signOut, startSignIn } from '../services/sessionApi';

export function useSessionController() {
  const queryClient = useQueryClient();
  const me = useQuery({ queryKey: ['me'], queryFn: fetchMeContext });
  const logout = useMutation({ mutationFn: signOut, onSuccess: () => queryClient.setQueryData(['me'], null) });
  return {
    loading: me.isPending,
    displayName: me.data?.user.displayName ?? null,
    failed: signInFailed(),
    signIn: startSignIn,
    signOut: () => logout.mutate(),
    signingOut: logout.isPending,
  };
}
