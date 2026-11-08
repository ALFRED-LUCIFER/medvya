import { describe, expect, it } from 'vitest';
import { baselineBody, baselineSchema, checkInGap, deliveryPercent } from './pilotMetrics';

describe('pilot metrics', () => {
  it('counts delivery over messages with an outcome only', () => {
    expect(deliveryPercent({ channels: [
      { channel: 'Email', total: 10, delivered: 7, failed: 1, pending: 2 },
      { channel: 'Sms', total: 4, delivered: 2, failed: 1, pending: 1 },
    ] })).toEqual({ percent: 81.8, delivered: 9, settled: 11 });
    expect(deliveryPercent({ channels: [{ channel: 'Email', total: 3, delivered: 0, failed: 0, pending: 3 }] }).percent).toBeNull();
  });

  it('reports the gap to the 60 s check-in target, or none without returning patients', () => {
    const base = { returningCount: 2, medianSeconds: 70, count: 3, targetSeconds: 60 };
    expect(checkInGap({ ...base, returningMedianSeconds: 74.6 })).toBe(15);
    expect(checkInGap({ ...base, returningMedianSeconds: 41 })).toBe(0);
    expect(checkInGap({ ...base, returningMedianSeconds: null })).toBeNull();
  });

  it('checks a baseline before sending it as a number', () => {
    const ok = { metric: 'NoShowPercent', value: '12', method: ' Paper register ', fromDate: '2026-10-05', toDate: '2026-10-18' } as const;
    expect(baselineSchema.safeParse(ok).success).toBe(true);
    expect(baselineBody('f1', baselineSchema.parse(ok))).toEqual({ facilityId: 'f1', metric: 'NoShowPercent', value: 12, method: 'Paper register', fromDate: '2026-10-05', toDate: '2026-10-18' });
    const issues = (input: object) => baselineSchema.safeParse({ ...ok, ...input }).error?.issues.map(i => i.path.join('.'));
    expect(issues({ value: '120' })).toEqual(['value']);
    expect(issues({ metric: 'CheckInSeconds', value: '120' })).toBeUndefined(); // seconds are not a percentage
    expect(issues({ value: '-1' })).toEqual(['value']);
    expect(issues({ method: '  ' })).toEqual(['method']);
    expect(issues({ toDate: '2026-10-01' })).toEqual(['toDate']);
  });
});
