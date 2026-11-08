import { useQuery } from '@tanstack/react-query';
import { errorKey } from '../../api/domain/errors';
import { useSignedIn } from '../../session/hooks/session';
import { tabsFor } from '../../session/domain/session';
import { orderQueue } from '../domain/today';
import { fetchToday } from '../services/today';

export function useTodayController() {
  const { session } = useSignedIn();
  const query = useQuery({ queryKey: ['today', session.facility.id], queryFn: () => fetchToday(session.facility.id) });
  return {
    session,
    today: query.data ? { ...query.data, queue: orderQueue(query.data.queue) } : null,
    loading: query.isPending,
    error: query.error ? errorKey(query.error) : null,
    refreshing: query.isRefetching,
    onRefresh: () => void query.refetch(),
    /** Starting a consultation needs the Consult tab, which front desk does not have. */
    canConsult: tabsFor(session.staff.role).includes('consult'),
  };
}
