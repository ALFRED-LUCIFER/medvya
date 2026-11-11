import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../../patients/services/patientsApi';
import { completeRegistration } from './registrationApi';

afterEach(() => vi.unstubAllGlobals());

describe('complete registration', () => {
  const request = { address: null, payer: { kind: 'SelfPay' as const, payerName: null, plan: null, memberNumber: null, validTo: null, network: null }, emergencyContact: null };

  it('sends back the version the record was read at (#889)', async () => {
    const fetch = vi.fn(async (_url: string, _init?: RequestInit) => Response.json({ status: 'Incomplete' }));
    vi.stubGlobal('fetch', fetch);
    await completeRegistration('5e1d0001-0004-4000-8000-000000000001', request, 7);
    expect(JSON.parse(fetch.mock.calls[0]![1]!.body as string)).toMatchObject({ version: 7 });
  });

  it('reports a stale save as a 409', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => Response.json({ code: 'changed' }, { status: 409 })));
    const error = await completeRegistration('5e1d0001-0004-4000-8000-000000000001', request, 7).catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ status: 409 });
  });
});
