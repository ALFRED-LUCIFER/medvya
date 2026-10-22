import { describe, expect, it } from 'vitest';
import { duplicateQuery, emptyQuickForm, errorCode, formField, quickSchema, resolves, toRegisterRequest, type DuplicateCandidate, type QuickForm } from './registration';

// Synthetic values only (IDs fail their checksums).
const deepak: QuickForm = { ...emptyQuickForm, fullName: 'Deepak Nair', mobile: '+91 90000 00201', dateOfBirth: '1979-08-14', sexAtBirth: 'Male' };
const candidate = (patientId: string): DuplicateCandidate => ({
  patientId, initials: 'DN', ageYears: 47, sexAtBirth: 'Male', mrnTail: '0872', matchedOnNameAndDateOfBirth: true, matchedIdentifiers: [], identifiers: [],
});

describe('duplicate check while typing', () => {
  it('compares name + date of birth and complete IDs, never the phone number', () => {
    expect(duplicateQuery(deepak, 'IN')).toEqual({ fullName: 'Deepak Nair', dateOfBirth: '1979-08-14', identifiers: [] });
    expect(JSON.stringify(duplicateQuery(deepak, 'IN'))).not.toContain('90000');
    expect(duplicateQuery({ ...deepak, identifier: '91-0000-0000-0301' }, 'IN')?.identifiers).toEqual([{ scheme: 'Abha', value: '91-0000-0000-0301' }]);
    expect(duplicateQuery({ ...deepak, identifier: '91-0000-00' }, 'IN')?.identifiers).toEqual([]); // half-typed: not sent
  });

  it('never sends a stated age as a date of birth, and waits for two letters of name', () => {
    expect(duplicateQuery({ ...deepak, ageInstead: true, ageYears: '47' }, 'IN')).toBeNull();
    expect(duplicateQuery({ ...deepak, fullName: 'D' }, 'IN')).toBeNull();
    expect(duplicateQuery({ ...deepak, dateOfBirth: '' }, 'IN')).toBeNull();
    // An Emirates ID alone is enough to compare.
    expect(duplicateQuery({ ...emptyQuickForm, identifier: '784-1988-1234567-1' }, 'AE')?.identifiers).toEqual([{ scheme: 'EmiratesId', value: '784-1988-1234567-1' }]);
  });

  it('allows saving only when the decision covers every possible match shown', () => {
    expect(resolves(null, [])).toBe(true);
    expect(resolves(null, [candidate('a')])).toBe(false);
    expect(resolves({ decision: 'NotSamePerson', candidateIds: ['a'] }, [candidate('a'), candidate('b')])).toBe(false);
    expect(resolves({ decision: 'MergeReview', candidateIds: ['a', 'b'] }, [candidate('a'), candidate('b')])).toBe(true);
  });
});

describe('quick registration form', () => {
  const errors = (country: 'IN' | 'AE', v: QuickForm) => {
    const r = quickSchema(country).safeParse(v);
    return r.success ? {} : Object.fromEntries(r.error.issues.map(i => [i.path.join('.'), i.message]));
  };

  it('needs name, mobile, date of birth or (India) age, and sex', () => {
    expect(errors('IN', emptyQuickForm)).toEqual({ fullName: 'required', mobile: 'required', dateOfBirth: 'required', sexAtBirth: 'required' });
    expect(errors('IN', { ...deepak, ageInstead: true, ageYears: '131' })).toEqual({ ageYears: 'out_of_range' });
    expect(errors('IN', deepak)).toEqual({});
  });

  it('asks the UAE desk for the Emirates ID in its printed format', () => {
    expect(errors('AE', deepak)).toEqual({ identifier: 'required' });
    expect(errors('AE', { ...deepak, identifier: '784-1988-123' })).toEqual({ identifier: 'format.EmiratesId' });
    expect(errors('AE', { ...deepak, identifier: '784198812345671' })).toEqual({});
  });

  it('sends the decision with the registration and the age only when the date is unknown', () => {
    const r = toRegisterRequest({ ...deepak, ageInstead: true, ageYears: '47' }, 'IN', { decision: 'NotSamePerson', candidateIds: ['a'] });
    expect(r).toMatchObject({ quick: true, dateOfBirth: null, ageYears: 47, identifiers: [], duplicates: { decision: 'NotSamePerson' } });
    expect(toRegisterRequest({ ...deepak, ageInstead: true, ageYears: '47' }, 'AE', null)).toMatchObject({ dateOfBirth: '1979-08-14', ageYears: null });
  });

  it('maps server field paths to form fields and unknown codes to the generic message', () => {
    expect([formField('identifiers[0]'), formField('identifiers'), formField('mobile'), formField('address.city')]).toEqual(['identifier', 'identifier', 'mobile', null]);
    expect([errorCode('format.Abha'), errorCode('aadhaar_full_number_not_allowed')]).toEqual(['format.Abha', 'generic']);
  });
});
