import { describe, expect, it } from 'vitest';
import { all, defaultFilters, exportBody, listQuery, rangeOf } from './auditLog';

describe('access log filters', () => {
  it('defaults to the last 30 days, counted in the clinic time zone', () => {
    const f = defaultFilters(new Date('2026-11-05T12:00:00Z'), 'Asia/Kolkata');
    expect([f.from, f.to, f.zone]).toEqual(['2026-10-06', '2026-11-05', 'Asia/Kolkata']);
    // 20:30 UTC is already the next day in India (02:00 on the 6th), still the same day in London.
    expect(defaultFilters(new Date('2026-11-05T20:30:00Z'), 'Asia/Kolkata').to).toBe('2026-11-06');
    expect(defaultFilters(new Date('2026-11-05T20:30:00Z'), 'Europe/London').to).toBe('2026-11-05');
  });

  it('turns the chosen days into a half-open range in the clinic time zone, whatever the browser zone', () => {
    const day = { ...defaultFilters(), from: '2026-10-01', to: '2026-10-01' };
    expect(rangeOf({ ...day, zone: 'Asia/Kolkata' })).toEqual({ from: '2026-09-30T18:30:00.000Z', to: '2026-10-01T18:30:00.000Z' });
    expect(rangeOf({ ...day, zone: 'Asia/Dubai' })).toEqual({ from: '2026-09-30T20:00:00.000Z', to: '2026-10-01T20:00:00.000Z' });
    // The day daylight saving ends is 25 hours long; the range still runs midnight to midnight.
    expect(rangeOf({ ...defaultFilters(), from: '2026-11-01', to: '2026-11-01', zone: 'America/New_York' }))
      .toEqual({ from: '2026-11-01T04:00:00.000Z', to: '2026-11-02T05:00:00.000Z' });
  });

  it('sends only the filters that were set, and never a name', () => {
    const base = { ...defaultFilters(), from: '', to: '' };
    expect(listQuery({ ...base, actor: all }, null)).toBe('');
    expect(listQuery({ ...base, actor: 'u1', action: 'patient.read' }, 'c1')).toBe('actorUserId=u1&action=patient.read&after=c1');
    expect(exportBody({ ...base, patientId: 'p1' })).toMatchObject({ actorUserId: null, action: null, patientId: 'p1' });
  });
});
