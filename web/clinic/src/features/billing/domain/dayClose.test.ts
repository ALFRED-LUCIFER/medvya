import { describe, expect, it } from 'vitest';
import { countedMinor, dayCloseProblem, floatMinor, fromCounts, openSchema, toCounts, variance } from './dayClose';

const inr = [50_000, 20_000, 10_000, 5_000, 2_000, 1_000, 500, 200, 100];

describe('Day close (MDV-P4-012)', () => {
  it('adds the typed notes and coins in minor units; a blank or invalid count is 0', () => {
    expect(countedMinor(inr, { 50000: '8', 20000: '4', 10000: '9', 100: '3' })).toBe(400_000 + 80_000 + 90_000 + 300);
    expect(countedMinor(inr, { 50000: '', 20000: '-2', 10000: '1.5', 500: 'x' })).toBe(0);
    expect(toCounts([50_000, 100], { 50000: ' 2 ' })).toEqual([{ valueMinor: 50_000, count: 2 }, { valueMinor: 100, count: 0 }]);
    expect(fromCounts([{ valueMinor: 50_000, count: 2 }])).toEqual({ 50000: '2' });
  });

  it('shows a short drawer as a negative variance', () => {
    expect(variance(100_000, 120_000)).toBe(-20_000);
    expect(variance(120_000, 120_000)).toBe(0);
  });

  it('takes a float in rupees and paise', () => {
    expect(openSchema.safeParse({ float: '1000' }).success).toBe(true);
    expect(floatMinor({ float: '1000.50' })).toBe(100_050);
    for (const float of ['', '-1', '1.234', 'ten']) expect(openSchema.safeParse({ float }).success, float).toBe(false);
  });

  it('words a refusal by its reason', () => {
    expect(dayCloseProblem('confirm:same_user')).toBe('same_user');
    expect(dayCloseProblem('note:required')).toBe('required');
    expect(dayCloseProblem('session:already_open')).toBe('already_open');
    expect(dayCloseProblem('state:invalid')).toBe('state');
    expect(dayCloseProblem('changed')).toBe('stale');
    expect(dayCloseProblem(undefined)).toBe('failed');
  });
});
