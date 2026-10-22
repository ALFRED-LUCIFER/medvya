import { QueryClient } from '@tanstack/react-query';

/** A 4xx is the server's answer (invalid, refused, not found), not an outage. */
const isAnswer = (error: unknown) => {
  const status = (error as { status?: unknown } | null)?.status;
  return typeof status === 'number' && status < 500;
};

/**
 * Every /v1 read is audited, so the TanStack defaults would write audit rows nobody asked for: a refetch each time the tab
 * regains focus, and three retries of a refused (403) request. Screens that must stay current poll or invalidate on purpose.
 */
export const createQueryClient = () => new QueryClient({
  defaultOptions: {
    queries: {
      refetchOnWindowFocus: false,
      retry: (failures, error) => !isAnswer(error) && failures < 2,
    },
  },
});
