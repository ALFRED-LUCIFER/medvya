import { describe, expect, it } from 'vitest';
import en from '../../../../../../packages/i18n/messages/en.json';
import { editFor, featureChoices, featureLabel, formFor, fromMinor, groupPlans, knownFeatures, planForm, toMinor, type PlanVersion } from './plans';

const v = (code: string, version: number, extra: Partial<PlanVersion> = {}): PlanVersion => ({
  id: `${code}-${version}`, code, version, perDoctor: false, custom: false, doctors: 1, staff: 2, locations: 1, assistVisitsPerDoctorMonth: 300, trialDays: 14,
  features: ['patient_app.booking'], createdAt: '2026-11-06T00:00:00Z', createdBy: null, reason: null,
  prices: [{ country: 'IN', currency: 'INR', monthly: 199900, yearly: 1999000, assistMonthly: 99900 }, { country: 'AE', currency: 'AED', monthly: 34950, yearly: null, assistMonthly: null }],
  ...extra,
});

describe('plans', () => {
  it('keeps money in minor units both ways, without float rounding', () => {
    expect([toMinor('1999'), toMinor('349.5'), toMinor('0.07'), toMinor(' '), toMinor('2500000')]).toEqual([199900, 34950, 7, null, 250000000]);
    expect([fromMinor(199900), fromMinor(34950), fromMinor(7), fromMinor(null)]).toEqual(['1999', '349.50', '0.07', '']);
  });

  it('round-trips a version through the form into the next version\'s body', () => {
    const base = v('solo', 3, { doctors: null });
    const form = { ...formFor(base), reason: 'New price list approved' };
    expect(planForm.safeParse(form).success).toBe(true);
    expect(editFor(base, form)).toEqual({
      baseVersion: 3, doctors: null, staff: 2, locations: 1, assistVisitsPerDoctorMonth: 300, trialDays: 14, features: ['patient_app.booking'],
      prices: [{ country: 'IN', monthly: 199900, yearly: 1999000, assistMonthly: 99900 }, { country: 'AE', monthly: 34950, yearly: null, assistMonthly: null }],
      reason: 'New price list approved',
    });
  });

  it('refuses amounts and counts the server would refuse', () => {
    const ok = { ...formFor(v('solo', 1)), reason: 'New price list approved' };
    for (const bad of [
      { ...ok, prices: [{ ...ok.prices[0], monthly: '-1' }, ok.prices[1]] },
      { ...ok, prices: [{ ...ok.prices[0], monthly: '19.999' }, ok.prices[1]] },
      { ...ok, doctors: '0' }, { ...ok, staff: '1.5' }, { ...ok, trialDays: '91' }, { ...ok, trialDays: '' }, { ...ok, reason: 'ok' },
    ]) expect(planForm.safeParse(bad).success).toBe(false);
  });

  it('takes the newest version of each plan as current', () => {
    const plans = groupPlans([v('clinic', 2), v('clinic', 1), v('solo', 1)]);
    expect(plans.map(p => [p.code, p.current.version, p.history.length])).toEqual([['clinic', 2, 2], ['solo', 1, 1]]);
  });

  it('names every known feature and keeps an unknown key a plan already holds', () => {
    for (const k of knownFeatures) expect(en).toHaveProperty([featureLabel(k)!]);
    expect(featureLabel('beta.thing')).toBeNull();
    expect(featureChoices(v('solo', 1, { features: ['beta.thing'] })).at(-1)).toBe('beta.thing');
  });
});
