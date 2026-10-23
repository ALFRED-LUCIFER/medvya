// @vitest-environment jsdom
// A browser environment: on a server TanStack Query never refetches on focus or retries, which would hide both defaults.
import { describe, expect, it, vi } from 'vitest';
import { focusManager, QueryObserver } from '@tanstack/react-query';
import { createQueryClient } from './queryClient';

class Refused extends Error {
  readonly status = 403;
}

/** Subscribes like a mounted screen, then the user switches back to the tab; returns how often the query was fetched. */
async function fetchesAround(queryFn: () => Promise<unknown>) {
  const client = createQueryClient();
  client.mount(); // as QueryClientProvider does: listens for focus and reconnect
  const observer = new QueryObserver(client, { queryKey: ['audited'], queryFn });
  let settled = () => {};
  const firstAnswer = new Promise<void>(resolve => { settled = resolve; });
  const unsubscribe = observer.subscribe(r => { if (r.status !== 'pending' && r.fetchStatus === 'idle') settled(); });
  await firstAnswer;
  focusManager.setFocused(false);
  focusManager.setFocused(true);
  await new Promise(r => setTimeout(r, 50));
  unsubscribe();
  client.unmount();
  client.clear();
  return vi.mocked(queryFn).mock.calls.length;
}

describe('audited reads', () => {
  it('are fetched once: regaining tab focus does not read (and audit) them again', async () => {
    expect(await fetchesAround(vi.fn().mockResolvedValue({ id: 'p-1' }))).toBe(1);
  });

  it('are not retried when the server refuses them, so one denial is one audit row', async () => {
    expect(await fetchesAround(vi.fn().mockRejectedValue(new Refused()))).toBe(1);
  });
});
