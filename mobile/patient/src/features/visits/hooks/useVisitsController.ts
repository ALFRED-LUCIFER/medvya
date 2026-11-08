import { useQuery } from '@tanstack/react-query';
import { errorKey } from '../../api/domain/errors';
import { useSignedIn } from '../../session/hooks/session';
import { splitVisits } from '../domain/visits';
import { fetchVisits } from '../services/visits';

export function useVisitsController() {
  const { session } = useSignedIn();
  // Split when fetched: "now" is the time of the data, and a refresh moves visits that have passed.
  const query = useQuery({ queryKey: ['visits', session.person.id], queryFn: async () => splitVisits(await fetchVisits(session.person.id), new Date()) });
  return {
    session,
    visits: query.data ?? null,
    loading: query.isPending,
    error: query.error ? errorKey(query.error) : null,
  };
}
