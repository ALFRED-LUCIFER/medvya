import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../../patients/services/patientsApi';
import { decideMerge } from './mergeApi';

afterEach(() => vi.unstubAllGlobals());

describe('merge api', () => {
  it('posts a decision with the CSRF header and only the opaque id in the URL', async () => {
    const fetch = vi.fn(async (_url: string, _init?: RequestInit) => new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetch);
    await decideMerge('5e1d0001-0004-4000-8000-000000000001', 'requestApproval');
    const [url, init] = fetch.mock.calls[0]!;
    expect(url).toBe('/v1/patients/merge-requests/5e1d0001-0004-4000-8000-000000000001/request-approval');
    expect(init?.method).toBe('POST');
    expect(init!.headers).toMatchObject({ 'X-Medvya-Csrf': '1' });
  });

  it('reports a refusal by status and code', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => Response.json({ errors: [{ field: 'merge', code: 'same_approver' }] }, { status: 400 })));
    const error = await decideMerge('5e1d0001-0004-4000-8000-000000000001', 'approve').catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ status: 400, code: 'same_approver' });
  });
});
