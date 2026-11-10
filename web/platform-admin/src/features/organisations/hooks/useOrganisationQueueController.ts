import { useQuery } from '@tanstack/react-query';
import { useSearchParams } from 'react-router';
import { isFilter, type QueueFilter } from '../domain/organisations';
import { fetchQueue } from '../services/organisationsApi';
import { ApiError } from '../../api/services/api';

/** The queue and its filter, kept in the address (?filter=…) so a reload or a shared link opens the same list. */
export function useOrganisationQueueController() {
  const [params, setParams] = useSearchParams();
  const raw = params.get('filter');
  const filter: QueueFilter = isFilter(raw) ? raw : 'Waiting';
  const queue = useQuery({ queryKey: ['platform', 'organisations', filter], queryFn: () => fetchQueue(filter) });
  const items = queue.data ?? [];
  return {
    filter,
    onFilter: (f: QueueFilter) => setParams(f === 'Waiting' ? {} : { filter: f }),
    state: queue.isPending ? 'loading' as const : queue.isError ? 'error' as const : items.length === 0 ? 'empty' as const : 'ready' as const,
    forbidden: queue.error instanceof ApiError && queue.error.status === 403,
    items,
  };
}
