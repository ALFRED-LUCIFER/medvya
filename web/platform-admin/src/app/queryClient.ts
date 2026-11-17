import { MutationCache, QueryCache, QueryClient } from '@tanstack/react-query';
import { sessionQueryKey } from '../features/session/hooks/useSessionController';

const status = (e: unknown) => (e as { status?: unknown } | null)?.status;

/**
 * Reads are few and every refused one is worth seeing: no focus refetch, no retry (apiFetch already retries a failed GET).
 * A 401 from any call re-reads the session, so an ended session shows the sign-in page at once instead of a broken screen.
 */
export function createQueryClient() {
  const recheck = (e: unknown) => {
    if (status(e) === 401) void client.invalidateQueries({ queryKey: sessionQueryKey, exact: true });
  };
  const client: QueryClient = new QueryClient({
    queryCache: new QueryCache({ onError: recheck }),
    mutationCache: new MutationCache({ onError: recheck }),
    defaultOptions: { queries: { refetchOnWindowFocus: false, retry: false } },
  });
  return client;
}
