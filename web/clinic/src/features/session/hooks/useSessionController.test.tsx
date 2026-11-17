// @vitest-environment jsdom
import { QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createQueryClient } from '../../../app/queryClient';
import { useSessionController } from './useSessionController';

const me = { user: { id: 'u-1', displayName: 'A. Joseph' }, memberships: [], active: null };

afterEach(() => { vi.unstubAllGlobals(); });

function mount(logoutStatus = 204) {
  vi.stubGlobal('fetch', vi.fn((url: string) => Promise.resolve(
    url === '/v1/me/context' ? new Response(JSON.stringify(me), { status: 200 })
      : new Response(null, { status: url === '/bff/logout' ? logoutStatus : 204 }))));
  const client = createQueryClient();
  const wrapper = ({ children }: { children: ReactNode }) => <QueryClientProvider client={client}>{children}</QueryClientProvider>;
  const hook = renderHook(() => useSessionController(), { wrapper });
  // Keys without a clinic in them: they must not survive a change of clinic or user.
  client.setQueryData(['patients', 'search', 'ravi'], [{ id: 'p-1' }]);
  client.setQueryData(['staff', 'list'], [{ id: 's-1' }]);
  return { client, hook };
}

// SAFETY: mutation check: removed the removeQueries call in choose.onSuccess / logout.onSettled -> these tests failed.
describe('the session controller and the cache', () => {
  it('drops the previous clinic\'s reads when another clinic is chosen', async () => {
    const { client, hook } = mount();
    await waitFor(() => expect(hook.result.current.loading).toBe(false));
    const done = vi.fn();
    act(() => hook.result.current.chooseFacility('m-2', done));
    await waitFor(() => expect(done).toHaveBeenCalled());
    expect(client.getQueryData(['patients', 'search', 'ravi'])).toBeUndefined();
    expect(client.getQueryData(['staff', 'list'])).toBeUndefined();
    expect(client.getQueryData(['me'])).toBeDefined();
  });

  it.each([204, 401])('drops every read on sign-out (logout answered %i)', async status => {
    const { client, hook } = mount(status);
    await waitFor(() => expect(hook.result.current.loading).toBe(false));
    act(() => hook.result.current.signOut());
    await waitFor(() => expect(hook.result.current.signingOut).toBe(false));
    expect(client.getQueryData(['patients', 'search', 'ravi'])).toBeUndefined();
    expect(client.getQueryData(['staff', 'list'])).toBeUndefined();
  });
});
