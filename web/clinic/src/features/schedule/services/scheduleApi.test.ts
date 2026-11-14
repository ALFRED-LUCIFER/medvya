// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError, fetchScheduleDay } from './scheduleApi';

const day = { facilityId: 'f-1', date: '2026-10-19', columns: [] };
const answer = (status: number) => new Response(JSON.stringify(day), { status });
const early = (url: string, status = 200) => Promise.resolve({ url, res: answer(status) });

afterEach(() => { vi.unstubAllGlobals(); window.medvyaSchedule = undefined; });

// MDV-P1-110: public/prefetch.js starts the day's read on the schedule while the bundle downloads.
describe('fetchScheduleDay', () => {
  it('takes the read prefetch.js started for the same address, once, then reads afresh', async () => {
    const f = vi.fn().mockResolvedValue(answer(200));
    vi.stubGlobal('fetch', f);
    window.medvyaSchedule = early('/v1/schedule?facilityId=f-1&date=2026-10-19');
    expect(await fetchScheduleDay('f-1', '2026-10-19')).toEqual(day);
    expect(f).not.toHaveBeenCalled();
    expect(await fetchScheduleDay('f-1', '2026-10-19')).toEqual(day);
    expect(f).toHaveBeenCalledTimes(1);
  });

  it('keeps a prefetched refusal as the answer, not read again', async () => {
    vi.stubGlobal('fetch', vi.fn());
    window.medvyaSchedule = early('/v1/schedule?facilityId=f-1', 403);
    await expect(fetchScheduleDay('f-1', null)).rejects.toSatisfy(e => e instanceof ApiError && e.status === 403);
    expect(fetch).not.toHaveBeenCalled();
  });

  it.each([
    ['another facility', early('/v1/schedule?facilityId=f-2')],
    ['another day', early('/v1/schedule?facilityId=f-1&date=2026-10-20')],
    ['answered 503', early('/v1/schedule?facilityId=f-1', 503)],
    ['a failed read', Promise.resolve(undefined)],
  ])('reads again when the prefetch was %s', async (_, prefetched) => {
    const f = vi.fn().mockResolvedValue(answer(200));
    vi.stubGlobal('fetch', f);
    window.medvyaSchedule = prefetched;
    expect(await fetchScheduleDay('f-1', null)).toEqual(day);
    expect(f).toHaveBeenCalledTimes(1);
    expect(String(f.mock.calls[0]![0])).toBe('/v1/schedule?facilityId=f-1');
  });
});
