import { QueryClient } from '@tanstack/react-query';

/**
 * Every /v1 read is audited, so the TanStack defaults would write audit rows nobody asked for: a refetch each time the tab
 * regains focus, and three retries of a refused (403) request. Screens that must stay current poll or invalidate on purpose.
 * No query retry here: `apiFetch` already retries a GET twice with jitter on a timeout, a network error or a 502/503/504 (MDV-P1-048).
 * A second layer would turn one failing read into up to nine requests over a slow link.
 */
export const createQueryClient = () => new QueryClient({
  defaultOptions: {
    queries: {
      refetchOnWindowFocus: false,
      retry: false,
    },
  },
});
