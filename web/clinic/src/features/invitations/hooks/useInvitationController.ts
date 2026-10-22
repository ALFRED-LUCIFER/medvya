import { useQuery } from '@tanstack/react-query';
import { joinWithInvitation, previewInvitation } from '../services/invitationsApi';

/** F-06: the invitation behind /invite/:token and the Join action. */
export function useInvitationController(token: string) {
  const preview = useQuery({ queryKey: ['invitation', token], queryFn: () => previewInvitation(token), retry: false });
  return {
    loading: preview.isPending,
    failed: preview.isError,
    invitation: preview.data ?? null,
    retry: () => void preview.refetch(),
    join: () => joinWithInvitation(token),
  };
}
