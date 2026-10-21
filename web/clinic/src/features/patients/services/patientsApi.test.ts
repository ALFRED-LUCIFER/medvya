import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError, fetchPatient, searchPatients } from './patientsApi';

// Synthetic values only.
const phone = '+91 90000 00101';

afterEach(() => vi.unstubAllGlobals());

function stubFetch(response: Response) {
  const fetch = vi.fn(async (_url: string, _init?: RequestInit) => response);
  vi.stubGlobal('fetch', fetch);
  return fetch;
}

describe('patients api', () => {
  it('sends the search text in a POST body and never in the URL', async () => {
    const fetch = stubFetch(Response.json({ matchedOn: 'Phone', items: [], page: 1, pageSize: 20, total: 0 }));
    await searchPatients({ query: phone, filter: 'All', page: 1 });

    const [url, init] = fetch.mock.calls[0]!;
    expect(url).toBe('/v1/patients/search');
    expect(init?.method).toBe('POST');
    expect(JSON.parse(String(init?.body))).toEqual({ query: phone, filter: 'All', page: 1 });
    expect(init!.headers).toMatchObject({ 'X-Medvya-Csrf': '1' });
  });

  it('reports a refused search by status and code, without the text that was sent', async () => {
    stubFetch(Response.json({ errors: [{ field: 'query', code: 'full_number_required' }] }, { status: 400 }));
    const error = await searchPatients({ query: '98470 213', filter: 'All', page: 1 }).catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ status: 400, code: 'full_number_required' });
    expect(String((error as Error).message)).not.toContain('98470');
  });

  it('opens a record by its opaque id only', async () => {
    const fetch = stubFetch(new Response(null, { status: 403 }));
    await expect(fetchPatient('5e1d0001-0004-4000-8000-000000000001')).rejects.toMatchObject({ status: 403 });
    expect(fetch.mock.calls[0]![0]).toBe('/v1/patients/5e1d0001-0004-4000-8000-000000000001');
  });
});
