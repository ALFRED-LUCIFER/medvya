// @vitest-environment jsdom
import { QueryClientProvider } from '@tanstack/react-query';
import { renderHook, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { useNotificationsController } from '../features/notifications/hooks/useNotificationsController';
import { useSessionController } from '../features/session/hooks/useSessionController';
import { useTermsNotice } from '../features/settings/hooks/useTermsNotice';
import { createQueryClient } from './queryClient';

const answers: Record<string, unknown> = {
  '/v1/me/context': { user: { id: 'u-1', displayName: 'A. Joseph' }, memberships: [], active: null },
  '/v1/notifications/inbox?facilityId=f-1': { unread: 0, items: [] },
  '/v1/settings/terms': { required: true, accepted: false },
};

afterEach(() => { vi.unstubAllGlobals(); });

/** Mounts the hook, lets it answer, then mounts it again as a second screen would; returns the requests made, by address. */
async function requestsOfTwoMounts<T>(hook: () => T, answered: (result: T) => boolean) {
  const f = vi.fn((url: string) => Promise.resolve(new Response(JSON.stringify(answers[url]), { status: 200 })));
  vi.stubGlobal('fetch', f);
  const client = createQueryClient();
  const wrapper = ({ children }: { children: ReactNode }) => <QueryClientProvider client={client}>{children}</QueryClientProvider>;
  const first = renderHook(hook, { wrapper });
  await waitFor(() => expect(answered(first.result.current)).toBe(true));
  const second = renderHook(hook, { wrapper });
  await new Promise(r => setTimeout(r, 20));
  first.unmount();
  second.unmount();
  client.clear();
  return f.mock.calls.map(c => c[0]);
}

// #1365: every screen read the session, the bell and the terms again; each is read once and then refreshed by its own trigger.
describe('what the shell reads', () => {
  it('reads the session once however many screens use it', async () => {
    expect(await requestsOfTwoMounts(() => useSessionController(), s => !s.loading)).toEqual(['/v1/me/context']);
  });

  it('reads the bell once', async () => {
    expect(await requestsOfTwoMounts(() => useNotificationsController('f-1', true), n => !n.loading)).toEqual(['/v1/notifications/inbox?facilityId=f-1']);
  });

  it('reads the terms notice once', async () => {
    expect(await requestsOfTwoMounts(() => useTermsNotice(true), pending => pending)).toEqual(['/v1/settings/terms']);
  });
});
