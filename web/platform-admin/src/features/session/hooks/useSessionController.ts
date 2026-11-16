import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ApiError } from '../../api/services/api';
import { keyProblemOf, linkFrom, stageOf } from '../domain/session';
import { currentFragment, fetchMe, registerKey, signOnFailed, signOut, startSignOn, touchKey } from '../services/sessionApi';

export const sessionQueryKey = ['me'] as const;

export function useSessionController() {
  const client = useQueryClient();
  const me = useQuery({ queryKey: sessionQueryKey, queryFn: fetchMe });
  const reread = () => client.invalidateQueries({ queryKey: sessionQueryKey });
  const check = useMutation({ mutationFn: touchKey, onSettled: reread });
  const register = useMutation({ mutationFn: registerKey, onSettled: reread });
  const leave = useMutation({ mutationFn: signOut });
  const link = linkFrom(currentFragment());
  return {
    loading: me.isPending,
    loadFailed: me.isError,
    me: me.data ?? null,
    stage: stageOf(me.data ?? null),
    hasLink: link !== null,
    signOnFailed: signOnFailed(),
    // The server names one refusal (a synced passkey); a cancelled prompt and every other refusal look the same to the member.
    keyProblem: register.isError ? keyProblemOf(register.error instanceof ApiError ? register.error.fields : null) : check.isError ? 'failed' as const : null,
    busy: check.isPending || register.isPending,
    signingOut: leave.isPending,
    onSignOn: () => void startSignOn(link),
    onTouchKey: () => check.mutate(),
    onRegisterKey: () => register.mutate(),
    onSignOut: () => leave.mutate(),
  };
}
