import { describe, expect, it } from 'vitest';
import { catalogs, loadCatalog, type Country } from '@medvya/i18n';
import {
  ageOn, completionSchema, dataMap, duplicateQuery, isMinor, eidExpired, emirates, emptyForm, errorCode, formField, hieFor, indiaStates, knownErrors, messageFor, payerFields,
  payerKinds, payerLabel, regionKey, registrationSchema, resolves, rules, stepOf, toCompletionRequest, toRegisterRequest, type DuplicateCandidate, type RegistrationForm,
} from './registration';

// Synthetic values only (IDs fail their checksums).
const lakshmi = { ecName: 'Lakshmi Nair', ecRelationship: 'Spouse', ecPhone: '+91 90000 00209' };
const deepak: RegistrationForm = { ...emptyForm, fullName: 'Deepak Nair', mobile: '+91 90000 00201', dateOfBirth: '1979-08-14', sexAtBirth: 'Male', ...lakshmi };
const fatima: RegistrationForm = {
  ...emptyForm, fullName: 'Fatima Al Mansoori', mobile: '050 000 0458', dateOfBirth: '1988-02-03', sexAtBirth: 'Female', emiratesId: '784-1988-1234567-1',
  ecName: 'Ahmed Al Mansoori', ecRelationship: 'Spouse', ecPhone: '050 000 0459',
};
const today = '2026-10-26';
const candidate = (patientId: string): DuplicateCandidate => ({
  patientId, initials: 'DN', ageYears: 47, sexAtBirth: 'Male', mrnTail: '0872', matchedOnNameAndDateOfBirth: true, matchedIdentifiers: [], identifiers: [],
});
const errors = (country: Country, quick: boolean, v: RegistrationForm) => {
  const r = registrationSchema(country, quick, today).safeParse(v);
  return r.success ? {} : Object.fromEntries(r.error.issues.map(i => [i.path.join('.'), i.message]));
};

describe('duplicate check while typing', () => {
  it('compares name + date of birth and complete IDs, never the phone number', () => {
    expect(duplicateQuery(deepak, 'IN', true)).toEqual({ fullName: 'Deepak Nair', dateOfBirth: '1979-08-14', identifiers: [] });
    expect(JSON.stringify(duplicateQuery(deepak, 'IN', false))).not.toContain('90000');
    expect(duplicateQuery({ ...deepak, abha: '91-0000-0000-0301' }, 'IN', true)?.identifiers).toEqual([{ scheme: 'Abha', value: '91-0000-0000-0301' }]);
    expect(duplicateQuery({ ...deepak, abha: '91-0000-00' }, 'IN', true)?.identifiers).toEqual([]); // half-typed: not sent
  });

  it('in full mode also compares the ABHA address and the passport, but never Aadhaar digits or a visa', () => {
    const full = { ...deepak, abhaAddress: 'deepak.synthetic@sbx', otherIdType: 'AadhaarLast4', otherIdValue: '0201' };
    expect(duplicateQuery(full, 'IN', false)?.identifiers).toEqual([{ scheme: 'AbhaAddress', value: 'deepak.synthetic@sbx' }]);
    expect(duplicateQuery({ ...fatima, passport: 'SYN12345', visa: 'SYN/201/2026' }, 'AE', false)?.identifiers)
      .toEqual([{ scheme: 'EmiratesId', value: '784-1988-1234567-1' }, { scheme: 'Passport', value: 'SYN12345' }]);
  });

  it('never sends a stated age as a date of birth, and waits for two letters of name', () => {
    expect(duplicateQuery({ ...deepak, ageInstead: true, ageYears: '47' }, 'IN', true)).toBeNull();
    expect(duplicateQuery({ ...deepak, fullName: 'D' }, 'IN', true)).toBeNull();
    expect(duplicateQuery({ ...deepak, dateOfBirth: '' }, 'IN', true)).toBeNull();
    expect(duplicateQuery({ ...emptyForm, emiratesId: '784-1988-1234567-1' }, 'AE', true)?.identifiers).toEqual([{ scheme: 'EmiratesId', value: '784-1988-1234567-1' }]);
  });

  it('allows saving only when the decision covers every possible match shown', () => {
    expect(resolves(null, [])).toBe(true);
    expect(resolves(null, [candidate('a')])).toBe(false);
    expect(resolves({ decision: 'NotSamePerson', candidateIds: ['a'] }, [candidate('a'), candidate('b')])).toBe(false);
    expect(resolves({ decision: 'MergeReview', candidateIds: ['a', 'b'] }, [candidate('a'), candidate('b')])).toBe(true);
  });
});

describe('quick registration', () => {
  it('needs name, mobile, date of birth or (India) age, and sex', () => {
    expect(errors('IN', true, emptyForm)).toEqual({ fullName: 'required', mobile: 'required', dateOfBirth: 'required', sexAtBirth: 'required' });
    expect(errors('IN', true, { ...deepak, ageInstead: true, ageYears: '131' })).toEqual({ ageYears: 'out_of_range' });
    expect(errors('IN', true, deepak)).toEqual({});
  });

  it('asks the UAE desk for the Emirates ID in its printed format', () => {
    expect(errors('AE', true, { ...fatima, emiratesId: '' })).toEqual({ emiratesId: 'required' });
    expect(errors('AE', true, { ...fatima, emiratesId: '784-1988-123' })).toEqual({ emiratesId: 'format.EmiratesId' });
    expect(errors('AE', true, { ...fatima, emiratesId: '784198812345671' })).toEqual({});
  });

  it('sends only the quick fields, with the age only when the date is unknown', () => {
    const typedMore = { ...deepak, ageInstead: true, ageYears: '47', email: 'x@example.in', line1: '12 Synthetic Street', payerKind: 'SelfPay' };
    const r = toRegisterRequest(typedMore, 'IN', true, { decision: 'NotSamePerson', candidateIds: ['a'] });
    expect(r).toMatchObject({ quick: true, dateOfBirth: null, ageYears: 47, identifiers: [], email: null, address: null, payer: null, duplicates: { decision: 'NotSamePerson' } });
    expect(toRegisterRequest({ ...deepak, ageInstead: true, ageYears: '47' }, 'AE', true, null)).toMatchObject({ dateOfBirth: '1979-08-14', ageYears: null });
  });
});

describe('full registration · India (MDV-P1-013)', () => {
  const address = { line1: '12/402 Synthetic Apartments', line2: 'Marine Drive', city: 'Kochi', district: 'Ernakulam', region: 'Kerala', postalCode: '682001' };

  it('needs only the minimum; Aadhaar is the last 4 digits and a full number is refused with a reason', () => {
    expect(errors('IN', false, deepak)).toEqual({});
    expect(errors('IN', false, { ...deepak, otherIdType: 'AadhaarLast4', otherIdValue: '0000 1111 2222' })).toEqual({ otherIdValue: 'aadhaar_full_number_not_allowed' });
    expect(errors('IN', false, { ...deepak, otherIdType: 'AadhaarLast4', otherIdValue: '12' })).toEqual({ otherIdValue: 'format.AadhaarLast4' });
    expect(errors('IN', false, { ...deepak, otherIdType: 'None', otherIdValue: '' })).toEqual({});
    expect(errors('IN', false, { ...deepak, abha: '9100' })).toEqual({ abha: 'format.Abha' });
  });

  it('checks the Indian address once any part is entered: PIN code must be 6 digits', () => {
    expect(errors('IN', false, { ...deepak, ...address })).toEqual({});
    expect(errors('IN', false, { ...deepak, ...address, postalCode: '68200' })).toEqual({ postalCode: 'format.pin' });
    expect(errors('IN', false, { ...deepak, postalCode: '682001' })).toEqual({ line1: 'required', city: 'required', region: 'required' });
  });

  it('asks each payer type for its own fields and sends only those', () => {
    expect(errors('IN', false, { ...deepak, payerKind: 'PrivateInsurance' })).toEqual({ payerName: 'required', memberNumber: 'required' });
    const insured = { ...deepak, ...address, payerKind: 'PrivateInsurance', payerName: 'Synthetic Health', network: 'Synthetic TPA', memberNumber: 'SYN-23-000201-01', validTo: '2027-03-31' };
    const r = toRegisterRequest({ ...insured, abha: '91-0000-0000-0201', otherIdType: 'AadhaarLast4', otherIdValue: '0201' }, 'IN', false, null);
    expect(r.payer).toEqual({ kind: 'PrivateInsurance', payerName: 'Synthetic Health', plan: null, memberNumber: 'SYN-23-000201-01', validTo: '2027-03-31', network: 'Synthetic TPA' });
    expect(r.address).toEqual({ ...address, makani: null, poBox: null });
    expect(r.identifiers).toEqual([{ scheme: 'Abha', value: '91-0000-0000-0201' }, { scheme: 'AadhaarLast4', value: '0201' }]);
    // Switching to self-pay never sends the insurance details still on the form.
    expect(toRegisterRequest({ ...insured, payerKind: 'SelfPay' }, 'IN', false, null).payer).toEqual(
      { kind: 'SelfPay', payerName: null, plan: null, memberNumber: null, validTo: null, network: null });
  });
});

describe('full registration · UAE (MDV-P1-014)', () => {
  it('needs an Emirates ID or a passport; a passport alone is enough', () => {
    expect(errors('AE', false, { ...fatima, emiratesId: '' })).toEqual({ emiratesId: 'emirates_id_or_passport_required' });
    expect(errors('AE', false, { ...fatima, emiratesId: '', passport: 'SYN12345' })).toEqual({});
  });

  it('needs the emirate once an address is entered; Makani is 10 digits', () => {
    expect(errors('AE', false, { ...fatima, line1: 'Villa 14' })).toEqual({ region: 'emirate_required' });
    expect(errors('AE', false, { ...fatima, line1: 'Villa 14', region: 'AUH', makani: '12345' })).toEqual({ makani: 'format.makani' });
  });

  it('sends the Arabic name, the EID expiry and the UAE address shape', () => {
    const r = toRegisterRequest({ ...fatima, arabicName: 'فاطمة المنصوري', emiratesIdExpiry: '2020-11-30', nationality: 'AE', line1: 'Villa 14', region: 'AUH', city: 'ignored', postalCode: '682001' }, 'AE', false, null);
    expect(r.arabicName).toBe('فاطمة المنصوري');
    expect(r.identifiers).toEqual([{ scheme: 'EmiratesId', value: '784-1988-1234567-1', expiresOn: '2020-11-30' }]);
    expect(r.address).toEqual({ line1: 'Villa 14', line2: null, city: null, district: null, region: 'AUH', postalCode: null, makani: null, poBox: null });
    expect(r.nationality).toBe('AE');
  });

  it('maps the emirate to its health information exchange and warns on an expired Emirates ID', () => {
    expect([hieFor('AUH'), hieFor('DXB'), hieFor('SHJ'), hieFor('FUJ'), hieFor('Kerala')]).toEqual(['Malaffi', 'NABIDH', 'Riayati', 'Riayati', null]);
    expect([eidExpired('2026-10-24', '2026-10-25'), eidExpired('2026-10-25', '2026-10-25'), eidExpired('', '2026-10-25')]).toEqual([true, false, false]);
  });
});

describe('server errors and display', () => {
  const request = { identifiers: [{ scheme: 'Abha' as const, value: '1' }, { scheme: 'Passport' as const, value: '2' }] };

  it('maps server field paths to the field that holds them', () => {
    expect([formField('identifiers[1]', request, 'IN'), formField('identifiers[1]', request, 'AE'), formField('identifiers', request, 'AE'),
      formField('address.postalCode', request, 'IN'), formField('payer.memberNumber', request, 'IN'), formField('duplicates', request, 'IN')])
      .toEqual(['otherIdValue', 'passport', 'emiratesId', 'postalCode', 'memberNumber', null]);
    expect([messageFor('postalCode', 'format'), messageFor('mobile', 'format'), messageFor('region', 'not_allowed')]).toEqual(['format.pin', 'format', 'not_allowed']);
    expect([errorCode('format.Abha'), errorCode('something_new')]).toEqual(['format.Abha', 'generic']);
    expect([stepOf('postalCode'), stepOf('memberNumber'), stepOf('abha')]).toEqual(['contact', 'payer', 'identity']);
  });

  it('computes the age on a day, counting the birthday', () => {
    expect([ageOn('1979-08-14', '2026-08-13'), ageOn('1979-08-14', '2026-08-14'), ageOn('2030-01-01', '2026-08-14'), ageOn('1979-08', '2026-08-14')]).toEqual([46, 47, null, null]);
  });

  it('has every message the registration screens can ask for, in English, Hindi and Arabic', async () => {
    await loadCatalog('hi'); await loadCatalog('ar');
    const keys = [
      ...knownErrors.map(e => `registration.error.${e}`),
      ...(['IN', 'AE'] as const).flatMap(c => payerKinds[c].flatMap(k => payerFields(c, k).map(f => payerLabel(c, k, f)))),
      ...indiaStates.map(s => regionKey('IN', s)), ...emirates.map(e => regionKey('AE', e)),
      ...[...rules.IN, ...rules.AE].map(r => `registration.rules.${r}`),
      ...Object.values(dataMap).flat().map(d => `registration.map.${d.key}`),
    ];
    for (const locale of ['en', 'hi', 'ar'] as const)
      expect(keys.filter(k => !k || !(k in catalogs[locale]!)), locale).toEqual([]);
  });
});

describe('completing a quick registration (MDV-P1-015)', () => {
  const completion = (country: Country, v: Partial<RegistrationForm>) => {
    const r = completionSchema(country).safeParse({ ...emptyForm, ...v });
    return r.success ? {} : Object.fromEntries(r.error.issues.map(i => [i.path.join('.'), i.message]));
  };

  it('needs the address, the payer or both, each checked by the country rules', () => {
    expect(completion('IN', {})).toEqual({ line1: 'nothing_to_complete' });
    expect(completion('IN', { payerKind: 'SelfPay' })).toEqual({});
    expect(completion('IN', { line1: '4 Synthetic Street', city: 'Chennai', region: 'Tamil Nadu', postalCode: '6000' })).toEqual({ postalCode: 'format.pin' });
    expect(completion('AE', { line1: 'Villa 14' })).toEqual({ region: 'emirate_required' });
    expect(completion('AE', { payerKind: 'PrivateInsurance', payerName: 'Synthetic Insurer' })).toEqual({ memberNumber: 'required' });
  });

  it('sends only the address, the payer and the emergency contact', () => {
    expect(toCompletionRequest({ ...emptyForm, fullName: 'not sent', payerKind: 'SelfPay' }, 'IN')).toEqual({
      address: null, payer: { kind: 'SelfPay', payerName: null, plan: null, memberNumber: null, validTo: null, network: null }, emergencyContact: null,
    });
  });
});

describe('family, emergency contact and guardian (MDV-P1-020)', () => {
  const ravi = { guardianName: 'Ravi Nair', guardianRelationship: 'Parent', guardianPhone: '+91 90000 00210' };
  const child = { ...deepak, dateOfBirth: '2016-05-01' };

  it('needs an emergency contact for full registration, not for quick', () => {
    const none = { ...deepak, ecName: '', ecRelationship: '', ecPhone: '' };
    expect(errors('IN', false, none)).toEqual({ ecName: 'required' });
    expect(errors('IN', true, none)).toEqual({});
    expect(errors('IN', false, { ...deepak, ecPhone: '' })).toEqual({ ecPhone: 'required' });
  });

  it('needs a guardian under 18, in quick mode too; the 18th birthday counts and a stated age decides', () => {
    expect(errors('IN', false, child)).toEqual({ guardianName: 'required_for_minor' });
    expect(errors('IN', true, child)).toEqual({ guardianName: 'required_for_minor' });
    expect(errors('IN', true, { ...child, ...ravi })).toEqual({});
    expect([isMinor({ ...deepak, dateOfBirth: '2008-10-26' }, 'IN', today), isMinor({ ...deepak, dateOfBirth: '2008-10-27' }, 'IN', today)]).toEqual([false, true]);
    expect(isMinor({ ...deepak, ageInstead: true, ageYears: '17' }, 'IN', today)).toBe(true);
    expect(isMinor({ ...deepak, dateOfBirth: '' }, 'IN', today)).toBeNull(); // unknown yet: not guessed
  });

  it('offers Sponsor in the UAE only', () => {
    expect(errors('IN', false, { ...deepak, ecRelationship: 'Sponsor' })).toEqual({ ecRelationship: 'required' });
    expect(errors('AE', false, { ...fatima, dateOfBirth: '2019-06-01', guardianName: 'Ahmed', guardianRelationship: 'Sponsor', guardianPhone: '050 000 0459' })).toEqual({});
  });

  it('sends the emergency contact in full mode and the guardian in both; maps their server errors', () => {
    const full = toRegisterRequest({ ...child, ...ravi }, 'IN', false, null);
    expect([full.emergencyContact, full.guardian]).toEqual([
      { name: 'Lakshmi Nair', relationship: 'Spouse', phone: '+91 90000 00209' }, { name: 'Ravi Nair', relationship: 'Parent', phone: '+91 90000 00210' }]);
    const quick = toRegisterRequest({ ...child, ...ravi }, 'IN', true, null);
    expect([quick.emergencyContact, quick.guardian?.name]).toEqual([null, 'Ravi Nair']);
    expect(['guardian', 'guardian.phone', 'emergencyContact.relationship'].map(f => formField(f, { identifiers: [] }, 'IN')))
      .toEqual(['guardianName', 'guardianPhone', 'ecRelationship']);
    expect(toCompletionRequest({ ...lakshmi }, 'IN').emergencyContact).toEqual({ name: 'Lakshmi Nair', relationship: 'Spouse', phone: '+91 90000 00209' });
  });
});
