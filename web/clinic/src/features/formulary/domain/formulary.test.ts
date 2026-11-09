import { describe, expect, it } from 'vitest';
import { classesFor, medicineSchema, orderSetSchema, splitProblem, toMedicineBody, toOrderSetBody } from './formulary';

const valid = { generic: ' Glimepiride ', strength: '2 mg', form: 'Tablet', brands: 'Amaryl, , Glimy', regClass: 'ScheduleH', nationalCode: '', atc: '', defaultSig: '' } as const;

describe('formulary rules', () => {
  it('offers only the classes of the country', () => {
    expect(classesFor('IN')).toEqual(['None', 'ScheduleH', 'ScheduleH1', 'ScheduleX']);
    expect(classesFor('AE')).toEqual(['None', 'Controlled']);
    expect(classesFor(null)).toEqual(['None']);
  });

  it('sends trimmed brands and leaves blank optional fields as null, never a guess', () => {
    const body = toMedicineBody(medicineSchema.parse(valid));
    expect(body).toMatchObject({ generic: 'Glimepiride', brands: ['Amaryl', 'Glimy'], nationalCode: null, atc: null, defaultSig: null });
  });

  it('refuses a missing name, strength, form or class before the round trip', () => {
    expect(medicineSchema.safeParse({ ...valid, generic: ' ' }).success).toBe(false);
    expect(medicineSchema.safeParse({ ...valid, form: 'Lozenge' }).success).toBe(false);
    expect(medicineSchema.safeParse({ ...valid, regClass: undefined }).success).toBe(false);
  });

  it('splits an order set into one test per line', () => {
    const body = toOrderSetBody(orderSetSchema.parse({ name: ' Diabetes review labs ', items: 'HbA1c\n\n Fasting glucose ' }));
    expect(body).toEqual({ name: 'Diabetes review labs', items: ['HbA1c', 'Fasting glucose'] });
    expect(orderSetSchema.safeParse({ name: 'x', items: '  ' }).success).toBe(false);
  });

  it('reads the server problem into a field and a code', () => {
    expect(splitProblem('regClass:not_in_country')).toEqual({ field: 'regClass', code: 'not_in_country' });
    expect(splitProblem('already_listed')).toEqual({ field: null, code: 'conflict' });
    expect(splitProblem(undefined)).toEqual({ field: null, code: 'failed' });
  });
});
