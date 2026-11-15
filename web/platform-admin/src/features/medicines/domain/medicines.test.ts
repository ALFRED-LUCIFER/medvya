import { describe, expect, it } from 'vitest';
import en from '../../../../../../packages/i18n/messages/en.json';
import { canWrite, classesFor, emptyForm, errorKeyOf, formOf, medicineForm, toBody, type Medicine } from './medicines';

const row: Medicine = {
  id: '1', country: 'IN', generic: 'Metformin', strength: '500 mg', form: 'Tablet', brands: ['Synthbrand'], nationalCode: null, regClass: 'NotVerified',
  atc: 'A10BA02', defaultSig: null, source: 'Medvya', registerCode: null, registerRelease: null, active: true,
};

describe('medicines (MDV-P5-064)', () => {
  it('lets only owner and operations change the list', () => {
    expect(['Owner', 'Operations', 'Support', 'Finance', 'Reviewer'].filter(r => canWrite(r as never))).toEqual(['Owner', 'Operations']);
  });

  it('offers each country its own classes, Not verified included', () => {
    expect(classesFor('IN')).toEqual(['None', 'ScheduleH', 'ScheduleH1', 'ScheduleX', 'NotVerified']);
    expect(classesFor('AE')).toEqual(['None', 'Controlled', 'NotVerified']);
  });

  it('starts a new medicine as Not verified and asks for a reason', () => {
    expect(emptyForm().regClass).toBe('NotVerified');
    expect(medicineForm.safeParse({ ...emptyForm(), generic: 'X', strength: '1 mg' }).success).toBe(false);
    expect(medicineForm.safeParse({ ...emptyForm(), generic: 'X', strength: '1 mg', reason: 'Advisor list' }).success).toBe(true);
  });

  it('round-trips a row into the correction form and sends blanks as null', () => {
    const body = toBody({ ...formOf(row), brands: 'A, , B', reason: 'Typo in brand' });
    expect(body).toMatchObject({ generic: 'Metformin', brands: ['A', 'B'], nationalCode: null, defaultSig: null, atc: 'A10BA02', regClass: 'NotVerified' });
  });

  it('never swaps an unknown form for another: the correction asks for one', () => {
    expect(medicineForm.safeParse({ ...formOf({ ...row, form: 'Lotion' }), reason: 'Fix the form' }).success).toBe(false);
  });

  it('has words for every code the server or the form gives', () => {
    for (const code of ['required', 'too_long', 'tooLong', 'too_many', 'unknown', 'invalid', 'not_in_country', 'already_listed', 'gone', 'reason.short', 'reason.long', 'anything'])
      expect(en).toHaveProperty([errorKeyOf(code)]);
  });
});
