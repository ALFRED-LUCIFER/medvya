import { describe, expect, it } from 'vitest';
import { gestation, gpal, readingsBody, readingsSchema, startBody, startSchema } from './pregnancy';

describe('gestation', () => {
  it('says 24 w 3 d, second trimester, for 171 days, and caps the bar at term', () => {
    expect(gestation({ gestationDays: 171 })).toEqual({ weeks: 24, days: 3, trimester: 2, percent: 61.1 });
    expect(gestation({ gestationDays: 97 }).trimester).toBe(1);
    expect(gestation({ gestationDays: 196 }).trimester).toBe(3);
    expect(gestation({ gestationDays: 300 }).percent).toBe(100);
  });
});

describe('gpal', () => {
  it('shows an unknown count as a dash, never as zero', () => {
    expect(gpal({ gravida: 2, para: 1, abortions: 0, living: 1 })).toBe('G2 P1 A0 L1');
    expect(gpal({ gravida: 1, para: null, abortions: null, living: null })).toBe('G1 P– A– L–');
  });
});

describe('the forms', () => {
  it('needs an LMP or a scan, and the scan its gestation', () => {
    expect(startSchema.safeParse({}).success).toBe(false);
    expect(startSchema.safeParse({ scanDate: '2026-10-01', scanWeeks: '' }).success).toBe(false);
    expect(startSchema.safeParse({ lmp: '2026-05-19' }).success).toBe(true);
  });

  it('sends a blank as null, never 0', () => {
    expect(startBody({ lmp: '2026-05-19', gravida: '2', para: '' })).toEqual({ lmp: '2026-05-19', scanDate: null, scanWeeks: null, scanDays: null, gravida: 2, para: null, abortions: null, living: null });
    expect(readingsBody({ fundalHeightCm: '24', fetalHeartRate: '' })).toEqual({ fundalHeightCm: 24, fetalHeartRate: null });
    expect(readingsSchema.safeParse({ fundalHeightCm: '', fetalHeartRate: '' }).success).toBe(false);
    expect(readingsSchema.safeParse({ fetalHeartRate: '400' }).success).toBe(false);
  });
});
