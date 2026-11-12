import { MutationCache, QueryCache, QueryClient } from '@tanstack/react-query';
import { sessionQueryKey } from '../features/session/hooks/useSessionController';

const status = (e: unknown) => (e as { status?: unknown } | null)?.status;

/**
 * Every /v1 read is audited, so the TanStack defaults would write audit rows nobody asked for: a refetch each time the tab
 * regains focus, and three retries of a refused (403) request. Screens that must stay current poll or invalidate on purpose.
 * No query retry here: `apiFetch` already retries a GET twice with jitter on a timeout, a network error or a 502/503/504 (MDV-P1-048).
 * A second layer would turn one failing read into up to nine requests over a slow link.
 *
 * A 401 or 403 from any call re-reads the session (F-05, MDV-P0-015): an ended session shows "Session expired" at once instead of
 * at the next minute poll, and a revoked or suspended membership drops the active facility. A refusal for a missing capability
 * leaves the context as it was, so the screen keeps its own message.
 */
export function createQueryClient() {
  const recheck = (e: unknown) => {
    if (status(e) === 401 || status(e) === 403) void client.invalidateQueries({ queryKey: sessionQueryKey, exact: true });
  };
  const client: QueryClient = new QueryClient({
    queryCache: new QueryCache({ onError: recheck }),
    mutationCache: new MutationCache({ onError: recheck }),
    defaultOptions: {
      queries: {
        refetchOnWindowFocus: false,
        retry: false,
      },
    },
  });
  // Practice settings change only by a save here, which updates or invalidates its own query: moving between Settings tabs (each tab mounts its
  // panel afresh) reuses what was read in the last minute instead of reading and auditing it again (MDV audit UX-12, UX-13).
  client.setQueryDefaults(['settings'], { staleTime: 60_000 });
  return client;
}
