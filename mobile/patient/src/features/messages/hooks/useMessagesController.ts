import { useQuery } from '@tanstack/react-query';
import { errorKey } from '../../api/domain/errors';
import { useSignedIn } from '../../session/hooks/session';
import { fetchMessages } from '../services/messages';

export function useMessagesController() {
  const { session } = useSignedIn();
  const query = useQuery({ queryKey: ['messages', session.person.id], queryFn: () => fetchMessages(session.person.id) });
  return {
    session,
    messages: query.data ?? null,
    loading: query.isPending,
    error: query.error ? errorKey(query.error) : null,
  };
}
