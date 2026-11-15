import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../../schedule/services/scheduleApi';
import { RequestTimeoutError } from '../../api/services/apiClient';
import { LogoUnconfirmedError, saveProfile, settleLogo } from './settingsApi';

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

// #1365 F-066: the upload was stored although its request got no answer in time, and the screen said nothing had changed.
describe('a logo change that failed or got no answer', () => {
  const profileAt = (version: number) => vi.stubGlobal('fetch', vi.fn(async () => Response.json({ name: form.name, hasLogo: true, version })));

  it('counts as done when the profile has moved on', async () => {
    profileAt(8);
    await expect(settleLogo(Promise.reject(new RequestTimeoutError()), 7)).resolves.toBeUndefined();
    await expect(settleLogo(Promise.reject(new ApiError(500)), 7)).resolves.toBeUndefined();
  });

  it('keeps the failure when the profile did not move', async () => {
    profileAt(7);
    await expect(settleLogo(Promise.reject(new RequestTimeoutError()), 7)).rejects.toBeInstanceOf(RequestTimeoutError);
    await expect(settleLogo(Promise.reject(new ApiError(500)), 7)).rejects.toMatchObject({ status: 500 });
  });

  it('is unconfirmed when the profile cannot be read either', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => Response.json({}, { status: 503 })));
    await expect(settleLogo(Promise.reject(new ApiError(500)), 7)).rejects.toBeInstanceOf(LogoUnconfirmedError);
  });

  it('passes a refusal on without asking again', async () => {
    const fetch = vi.fn();
    vi.stubGlobal('fetch', fetch);
    await expect(settleLogo(Promise.reject(new ApiError(400, 'logo:type')), 7)).rejects.toMatchObject({ code: 'logo:type' });
    expect(fetch).not.toHaveBeenCalled();
  });

  it('does nothing more when the change succeeded', async () => {
    const fetch = vi.fn();
    vi.stubGlobal('fetch', fetch);
    await settleLogo(Promise.resolve(), 7);
    expect(fetch).not.toHaveBeenCalled();
  });
});
