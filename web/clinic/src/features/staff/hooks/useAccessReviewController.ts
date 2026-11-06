import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { confirmAccess, fetchAccessReview } from '../services/staffApi';

/** The quarterly access review: the people with their last sign-in, and a Confirm per person. The server decides who may and records each decision. */
export function useAccessReviewController() {
  const queryClient = useQueryClient();
  const query = useQuery({ queryKey: ['staff', 'access-review'], queryFn: fetchAccessReview });
  const confirm = useMutation({ mutationFn: confirmAccess, onSettled: () => void queryClient.invalidateQueries({ queryKey: ['staff'] }) });
  return {
    review: query.data ?? null,
    loading: query.isPending,
    failed: query.isError,
    confirmingId: confirm.isPending ? confirm.variables : null,
    confirmFailed: confirm.isError,
    onConfirm: (membershipId: string) => confirm.mutate(membershipId),
  };
}
