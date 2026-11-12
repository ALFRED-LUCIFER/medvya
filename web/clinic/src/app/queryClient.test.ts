// @vitest-environment jsdom
// A browser environment: on a server TanStack Query never refetches on focus or retries, which would hide both defaults.
import { describe, expect, it, vi } from 'vitest';
import { focusManager, QueryObserver } from '@tanstack/react-query';
import { createQueryClient } from './queryClient';
import { sessionQueryKey } from '../features/session/hooks/useSessionController';

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

/** Mounts the session query, lets one call fail with `status`, and returns how often the session was read. */
async function sessionReadsAfter(status: number, via: 'query' | 'mutation') {
  const client = createQueryClient();
  const readSession = vi.fn().mockResolvedValue({ kind: 'signedIn' });
  const session = new QueryObserver(client, { queryKey: sessionQueryKey, queryFn: readSession });
  const unsubscribe = session.subscribe(() => {});
  await vi.waitFor(() => expect(readSession).toHaveBeenCalledTimes(1));
  const failure = Object.assign(new Error('api'), { status });
  if (via === 'query') await client.fetchQuery({ queryKey: ['some-screen'], queryFn: () => Promise.reject(failure) }).catch(() => {});
  else await client.getMutationCache().build(client, { mutationFn: () => Promise.reject(failure) }).execute(undefined).catch(() => {});
  await new Promise(r => setTimeout(r, 20));
  unsubscribe();
  client.clear();
  return readSession.mock.calls.length;
}

describe('an ended session or a lost membership (F-05)', () => {
  it.each([401, 403])('re-reads the session when a read answers %i', async s => {
    expect(await sessionReadsAfter(s, 'query')).toBe(2);
  });

  it.each([401, 403])('re-reads the session when a change answers %i', async s => {
    expect(await sessionReadsAfter(s, 'mutation')).toBe(2);
  });

  it('leaves the session alone for other errors', async () => {
    expect(await sessionReadsAfter(404, 'query')).toBe(1);
  });
});

describe('practice settings (MDV audit UX-12)', () => {
  it('are read once when a Settings tab is opened again within the minute', async () => {
    const client = createQueryClient();
    const queryFn = vi.fn().mockResolvedValue([]);
    await client.fetchQuery({ queryKey: ['settings', 'locations'], queryFn });
    await client.fetchQuery({ queryKey: ['settings', 'locations'], queryFn });
    await client.fetchQuery({ queryKey: ['patients', 'p-1'], queryFn });
    await client.fetchQuery({ queryKey: ['patients', 'p-1'], queryFn });
    expect(queryFn).toHaveBeenCalledTimes(3); // settings once; anything else is read each time
    client.clear();
  });
});
