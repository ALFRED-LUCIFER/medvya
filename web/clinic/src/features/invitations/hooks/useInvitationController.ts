import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { takeLinkToken } from '../../api/services/linkToken';
import { joinWithInvitation, previewInvitation } from '../services/invitationsApi';

/** F-06: the invitation behind /invite#t=… (the token in the fragment, never in a URL a server logs) and the Join action. */
export function useInvitationController() {
  const [token] = useState(takeLinkToken);
  const preview = useQuery({ queryKey: ['invitation', token], queryFn: () => previewInvitation(token), retry: false });
  return {
    loading: preview.isPending,
    failed: preview.isError,
    invitation: preview.data ?? null,
    retry: () => void preview.refetch(),
    join: () => void joinWithInvitation(token),
  };
}
