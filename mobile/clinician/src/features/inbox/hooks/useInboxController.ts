import { useQuery } from '@tanstack/react-query';
import { errorKey } from '../../api/domain/errors';
import { useSignedIn } from '../../session/hooks/session';
import { inboxCount } from '../domain/inbox';
import { fetchInbox } from '../services/inbox';

export function useInboxController() {
  const { session } = useSignedIn();
  const query = useQuery({ queryKey: ['inbox', session.staff.id], queryFn: fetchInbox });
  return {
    session,
    inbox: query.data ?? null,
    count: query.data ? inboxCount(query.data, session.staff.role === 'doctor') : 0,
    clinical: session.staff.role === 'doctor',
    loading: query.isPending,
    error: query.error ? errorKey(query.error) : null,
  };
}
