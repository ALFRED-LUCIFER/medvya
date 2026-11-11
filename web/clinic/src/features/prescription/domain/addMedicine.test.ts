import { describe, expect, it } from 'vitest';
import { checksFor, draftOf, medicinesOf, quantityOf, unitOf } from './addMedicine';
import type { RxChecks, SafetyAlert } from './checks';
import { invalidFields, type Medicine } from './prescription';

const med = (id: string, generic: string, strength: string, form = 'Tablet'): Medicine => ({ id, generic, strength, form, brands: [] });

describe('add medicine quantity (MDV-P2-039)', () => {
  it('counts tablets from how often and how long', () => {
    expect(quantityOf('1-0-1', '5d', 'tablet')).toBe(10);
    expect(quantityOf('1-1-1', '3d', 'capsule')).toBe(9);
    expect(quantityOf('1-0-0', '3m', 'tablet')).toBe(90);
    expect(quantityOf('0-0-1', '1m', 'tablet')).toBe(30);
  });
  it('supplies a month when ongoing, and counts weekly doses up to a whole week', () => {
    expect(quantityOf('1-0-1', 'ongoing', 'tablet')).toBe(60);
    expect(quantityOf('weekly', '3m', 'tablet')).toBe(13);
    expect(quantityOf('weekly', '3d', 'tablet')).toBe(1);
  });
  it('is one pack when needed, or for a form that is not counted', () => {
    expect(quantityOf('sos', '5d', 'tablet')).toBe('pack');
    expect(quantityOf('1-0-1', '5d', null)).toBe('pack');
    expect(unitOf('Inhaler')).toBeNull();
    expect(unitOf(' Capsule ')).toBe('capsule');
  });
});

describe('add medicine sig', () => {
  it('maps the presets onto the existing sig codes, which the composer accepts', () => {
    const d = draftOf(med('a', 'Metformin', '500 mg'), { often: '1-0-0', when: 'after_food', long: '5d', instructions: 'With breakfast' });
    expect(d).toEqual({ dose: '1', doseUnit: 'tablet', route: 'oral', frequency: 'MORNING', timing: 'after_food', durationDays: '5', quantity: '5', instructions: 'With breakfast' });
    expect(invalidFields(d)).toEqual([]);
  });
  it('leaves unchosen fields and an uncounted form blank, never guessed', () => {
    expect(draftOf(med('s', 'Salbutamol', '100 mcg/dose', 'Inhaler'), { often: '1-1-1', when: 'any', long: 'ongoing', instructions: '' }))
      .toEqual({ dose: '', doseUnit: '', route: '', frequency: 'TDS', timing: '', durationDays: '', quantity: '', instructions: '' });
    expect(draftOf(med('a', 'Metformin', '500 mg'), { often: null, when: null, long: null, instructions: '' }))
      .toMatchObject({ frequency: '', timing: '', durationDays: '', quantity: '' });
  });
  it('lists one row per medicine; its strengths are the rows that share generic and form', () => {
    const rows = [med('1', 'Atorvastatin', '10 mg'), med('2', 'Atorvastatin', '20 mg'), med('3', 'Amlodipine', '5 mg')];
    expect(medicinesOf(rows).map(m => m.id)).toEqual(['1', '3']);
  });
});

describe('add medicine checks', () => {
  const alert = (key: string, itemId: string | null, severity: 'Severe' | 'Moderate' = 'Severe') =>
    ({ key, kind: 'Allergy', severity, itemId, medicine: null, other: null, otherOnChart: false, code: null, substance: null, allergyStatus: null, dailyMicrograms: null, maxDailyMicrograms: null }) as SafetyAlert;
  const all: RxChecks = {
    ruleSet: 'r', ruleSetVersion: 'v', allergies: 'Listed', checkedAt: '', unresolved: 2,
    alerts: [alert('mine', 'a'), alert('other', 'b'), alert('patient', null, 'Moderate')],
    notChecked: [{ id: 'b', medicine: 'X', onChart: false, reason: 'FreeText' }], overrides: [],
  };
  it("keeps this line's findings and the patient-wide ones, and counts only its open severe ones", () => {
    const mine = checksFor(all, 'a');
    expect(mine.alerts.map(a => a.key)).toEqual(['mine', 'patient']);
    expect(mine.notChecked).toEqual([]);
    expect(mine.unresolved).toBe(1);
    expect(checksFor({ ...all, overrides: [{ key: 'mine', reason: 'r', ruleSetVersion: 'v', overriddenBy: 'u', overriddenAt: '' }] }, 'a').unresolved).toBe(0);
  });
});
