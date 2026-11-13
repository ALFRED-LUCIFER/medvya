import { describe, expect, it } from 'vitest';
import { brandProblems, classesFor, medicineSchema, orderSetSchema, parseBrandRows, splitProblem, toMedicineBody, toOrderSetBody } from './formulary';

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

  it('splits pasted brand rows on commas or tabs and names a row without five values', () => {
    const augmentin = { brand: 'Augmentin', generic: 'Amoxicillin and clavulanate', strength: '625 mg', form: 'Tablet', allergyClass: 'penicillins' };
    expect(parseBrandRows(' Augmentin, Amoxicillin and clavulanate ,625 mg,Tablet, penicillins \n\nAugmentin\tAmoxicillin and clavulanate\t625 mg\tTablet\tpenicillins\n'))
      .toEqual({ rows: [augmentin, augmentin], problems: [] });
    expect(parseBrandRows('Synthetic, Paracetamol, 500 mg, Tablet, none\nSynthetic, Paracetamol, 500 mg').problems).toEqual([{ row: 2, code: 'columns' }]);
    expect(parseBrandRows(' \n ').problems).toEqual([{ row: null, code: 'none' }]);
  });

  it('words the server refusal of brand rows once a row and code, counted from 1', () => {
    expect(brandProblems({ errors: { 'rows[7].brand': ['required'], 'rows[7].allergyClass': ['required'], 'rows[1].generic': ['not_listed'], 'rows[4].allergyClass': ['mismatch'] } }))
      .toEqual([{ row: 2, code: 'not_listed' }, { row: 5, code: 'mismatch' }, { row: 8, code: 'required' }]);
    expect(brandProblems({ errors: { rows: ['too_many'] } })).toEqual([{ row: null, code: 'too_many' }]);
    expect(brandProblems({ errors: { 'rows[0].brand': ['something_new'] } })).toEqual([{ row: null, code: 'failed' }]);
    expect(brandProblems(null)).toEqual([{ row: null, code: 'failed' }]);
  });

  it('reads the server problem into a field and a code', () => {
    expect(splitProblem('regClass:not_in_country')).toEqual({ field: 'regClass', code: 'not_in_country' });
    expect(splitProblem('already_listed')).toEqual({ field: null, code: 'conflict' });
    expect(splitProblem(undefined)).toEqual({ field: null, code: 'failed' });
  });
});
