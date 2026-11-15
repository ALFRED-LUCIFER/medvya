import { useI18n, type MessageKey } from '@medvya/i18n';
import { useQuery } from '@tanstack/react-query';
import { errorKey } from '../../api/domain/errors';
import { useOnline } from '../../api/hooks/useOnline';
import { useSignedIn } from '../../session/hooks/session';
import { tabsFor } from '../../session/domain/session';
import { orderQueue } from '../domain/today';
import { fetchToday } from '../services/today';

export function useTodayController() {
  const { locale, t } = useI18n();
  const { session } = useSignedIn();
  const online = useOnline();
  // Offline the query still runs ('always'): it reads the encrypted last copy instead of the network.
  const query = useQuery({ queryKey: ['today', session.facility.id, online], queryFn: () => fetchToday(session, online, c => t(`reasonCategory.${c}` as MessageKey)), networkMode: 'always' });
  const savedAt = query.data?.savedAt ?? null;
  return {
    session,
    today: query.data ? { ...query.data.today, queue: orderQueue(query.data.today.queue) } : null,
    loading: query.isPending,
    error: query.error ? errorKey(query.error) : null,
    refreshing: query.isRefetching,
    onRefresh: () => void query.refetch(),
    /** When this is the offline last copy: the time it was saved, in the facility's time zone. */
    offlineSince: savedAt === null ? null : new Date(savedAt).toLocaleTimeString(locale, { hour: 'numeric', minute: '2-digit', timeZone: session.facility.timeZone }),
    /** Starting a consultation needs the Consult tab, which front desk does not have, and a live list: the last copy is read-only. */
    canConsult: tabsFor(session.staff.role).includes('consult') && savedAt === null,
  };
}
