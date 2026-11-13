import { describe, expect, it } from 'vitest';
import { checksFor, closest, draftOf, medicinesOf, quantityOf, unitOf, withDefaults } from './addMedicine';
import type { RxChecks, SafetyAlert } from './checks';
import { invalidFields, type Medicine, type SigDraft, type SigField } from './prescription';

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

describe('did you mean (MDV-P1-088)', () => {
  const row = (id: string, generic: string, strength: string, brands: string[] = []): Medicine => ({ id, generic, strength, form: 'Tablet', brands });
  const rows = [
    row('1', 'Amlodipine', '5 mg'), row('2', 'Amoxicillin and clavulanate', '375 mg', ['Augmentin']), row('3', 'Amoxicillin and clavulanate', '625 mg', ['Augmentin']),
    row('4', 'Aspirin', '75 mg', ['Synthetic Augur']), row('5', 'Metformin', '500 mg'),
  ];

  it('offers the medicines whose generic or brand starts most like the typed name, every strength, best first', () => {
    expect(closest(' Augmentn ', rows).map(m => m.id)).toEqual(['2', '3', '4']); // the brand shares "augment", then "augu" shares three letters
    expect(closest('clavulanic', rows).map(m => m.id)).toEqual(['2', '3']); // a later word of the generic counts
    expect(closest('amoxil', rows, 1).map(m => m.id)).toEqual(['2', '3']); // one medicine, both strengths
  });

  it('offers nothing when fewer than three starting letters are shared: no guess', () => {
    expect(closest('Agumentin', rows)).toEqual([]);
    expect(closest('formin', rows)).toEqual([]); // never a match inside a word
    expect(closest('xyz', [])).toEqual([]);
  });
});

describe('add medicine checks', () => {
  const alert = (key: string, itemId: string | null, severity: 'Severe' | 'Moderate' = 'Severe') =>
    ({ key, kind: 'Allergy', severity, itemId, medicine: null, other: null, otherOnChart: false, code: null, substance: null, allergyStatus: null, dailyMicrograms: null, maxDailyMicrograms: null }) as SafetyAlert;
  const all: RxChecks = {
    ruleSet: 'r', ruleSetVersion: 'v', allergies: 'Listed', checkedAt: '', unresolved: 2,
    alerts: [alert('mine', 'a'), alert('other', 'b'), alert('patient', null, 'Moderate')],
    notChecked: [{ id: 'b', medicine: 'X', onChart: false, reason: 'FreeText' }], overrides: [],
    weightDoses: [{ itemId: 'b', medicine: 'X', weightHundredthsKg: 1420, mgPerKg: 15, doseMicrograms: 213_000, doseTenthsMl: 89, maxDosesPerDay: 4, maxDailyMicrograms: 852_000 }],
  };
  it("keeps this line's findings and the patient-wide ones, and counts only its open severe ones", () => {
    const mine = checksFor(all, 'a');
    expect(mine.alerts.map(a => a.key)).toEqual(['mine', 'patient']);
    expect(mine.notChecked).toEqual([]);
    expect(mine.weightDoses).toEqual([]);
    expect(mine.unresolved).toBe(1);
    expect(checksFor({ ...all, overrides: [{ key: 'mine', reason: 'r', ruleSetVersion: 'v', overriddenBy: 'u', overriddenAt: '' }] }, 'a').unresolved).toBe(0);
  });
});

describe('composer defaults (MDV-AUDIT-1094)', () => {
  const blank: SigDraft = { dose: '', doseUnit: '', route: '', frequency: '', timing: '', durationDays: '', quantity: '', instructions: '' };
  const edit = (form: string | null, before: SigDraft, field: SigField, value: string) => withDefaults({ form }, before, { ...before, [field]: value }, field);

  it('fills the unit and the oral route of a tablet or capsule, and leaves other forms and a chosen route alone', () => {
    expect(edit('Capsule', blank, 'dose', '1')).toMatchObject({ dose: '1', doseUnit: 'capsule', route: 'oral' });
    expect(edit('Syrup', blank, 'dose', '5')).toMatchObject({ doseUnit: '', route: '' });
    expect(edit('Tablet', { ...blank, route: 'sublingual' }, 'dose', '1')).toMatchObject({ doseUnit: '', route: 'sublingual' });
  });

  it('calculates the quantity from dose, frequency and days, rounded up, and never overwrites a typed one', () => {
    let d = edit('Tablet', blank, 'dose', '1');
    d = edit('Tablet', d, 'frequency', 'TDS');
    d = edit('Tablet', d, 'durationDays', '5');
    expect(d.quantity).toBe('15');
    d = edit('Tablet', d, 'dose', '0.5');
    expect(d.quantity).toBe('8'); // follows while it is the calculated one
    d = edit('Tablet', d, 'quantity', '20');
    d = edit('Tablet', d, 'durationDays', '7');
    expect(d.quantity).toBe('20'); // typed: stays
    expect(edit('Tablet', { ...blank, dose: '1', doseUnit: 'tablet', frequency: 'SOS', durationDays: '5' }, 'timing', 'after_food').quantity).toBe(''); // when needed: no count
  });
});
