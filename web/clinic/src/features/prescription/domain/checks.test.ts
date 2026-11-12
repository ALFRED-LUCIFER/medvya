import { describe, expect, it } from 'vitest';
import { amountOf, kgOf, lineSafety, lookOf, mlOf, overrideSchema, type RxChecks, type SafetyAlert } from './checks';

const alert = (over: Partial<SafetyAlert>): SafetyAlert => ({
  key: 'k', kind: 'Allergy', severity: 'Severe', itemId: 'a', medicine: 'Amoxicillin', other: null, otherOnChart: false, code: 'penicillins',
  substance: 'Penicillin', allergyStatus: 'Confirmed', dailyMicrograms: null, maxDailyMicrograms: null, measure: null, ...over,
});
const checks = (over: Partial<RxChecks>): RxChecks => ({
  ruleSet: 'medvya-curated', ruleSetVersion: 'v', allergies: 'Listed', alerts: [], notChecked: [], overrides: [], unresolved: 0, checkedAt: '', weightDoses: [], ...over,
});

describe('safety check wording', () => {
  it('shows a daily amount in mg from 1 mg and in mcg below it', () => {
    expect(amountOf(6_000_000)).toEqual({ unit: 'mg', value: '6000' });
    expect(amountOf(1500)).toEqual({ unit: 'mg', value: '1.5' });
    expect(amountOf(300)).toEqual({ unit: 'mcg', value: '300' });
  });

  it('shows a weight in kg and a liquid dose in mL from their stored tenths and hundredths', () => {
    expect([kgOf(1420), kgOf(900), mlOf(89), mlOf(50)]).toEqual(['14.2', '9', '8.9', '5']);
  });

  it('paints a severe alert critical until overridden, and a moderate one as attention', () => {
    expect(lookOf(alert({}), false)).toBe('critical');
    expect(lookOf(alert({}), true)).toBe('info');
    expect(lookOf(alert({ severity: 'Moderate' }), false)).toBe('attention');
  });

  it('gives each line the worst open finding and never says "no alerts" when allergies are not recorded', () => {
    const c = checks({ alerts: [alert({ key: 's' }), alert({ key: 'm', severity: 'Moderate', itemId: 'b' })], notChecked: [{ id: 'c', medicine: 'X', onChart: false, reason: 'FreeText' }] });
    expect(['a', 'b', 'c', 'd'].map(id => lineSafety(c, id))).toEqual(['severe', 'moderate', 'notChecked', 'clear']);
    expect(lineSafety({ ...c, overrides: [{ key: 's', reason: 'r', ruleSetVersion: 'v', overriddenBy: 'u', overriddenAt: '' }] }, 'a')).toBe('overridden');
    expect(lineSafety({ ...c, allergies: 'NotRecorded' }, 'd')).toBe('notChecked');
  });

  it('needs a reason of up to 200 characters', () => {
    expect(overrideSchema.safeParse({ reason: '  ' }).success).toBe(false);
    expect(overrideSchema.safeParse({ reason: 'x'.repeat(201) }).success).toBe(false);
    expect(overrideSchema.safeParse({ reason: 'Reviewed' }).success).toBe(true);
  });
});
