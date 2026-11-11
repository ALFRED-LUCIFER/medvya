import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../../schedule/services/scheduleApi';
import { saveProfile } from './settingsApi';

afterEach(() => vi.unstubAllGlobals());

const form = { name: 'Iyer Family Clinic (synthetic)', address: '', registrationNumber: '', registrationExpiresOn: '', taxNumber: '', phone: '', email: '' };

describe('save profile', () => {
  it('sends back the version the profile was read at (#889)', async () => {
    const fetch = vi.fn(async (_url: string, _init?: RequestInit) => Response.json({ name: form.name, hasLogo: false, version: 4 }));
    vi.stubGlobal('fetch', fetch);
    await saveProfile(form, 3);
    expect(JSON.parse(fetch.mock.calls[0]![1]!.body as string)).toMatchObject({ name: form.name, address: null, version: 3 });
  });

  it('reports a stale save as a 409 "changed"', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => Response.json({ code: 'changed' }, { status: 409 })));
    const error = await saveProfile(form, 3).catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ status: 409, code: 'changed' });
  });
});
