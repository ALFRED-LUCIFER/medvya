import { describe, expect, it } from 'vitest';
import type { Country } from '@medvya/i18n';
import samples from '@medvya/i18n/country-samples.json';
import { emptyForm, registrationSchema, type FormField, type RegistrationForm } from './registration';

// #1417: the same synthetic samples the server's CountrySamplesTests checks; the form must say "valid" exactly where the server does.
const bases: Record<Country, RegistrationForm> = {
  IN: { ...emptyForm, consentTreatment: true, fullName: 'Deepak Nair', mobile: '+91 90000 00201', dateOfBirth: '1979-08-14', sexAtBirth: 'Male', ecName: 'Lakshmi Nair', ecRelationship: 'Spouse', ecPhone: '+91 90000 00209' },
  AE: { ...emptyForm, consentTreatment: true, fullName: 'Fatima Al Mansoori', mobile: '050 000 0458', dateOfBirth: '1988-02-03', sexAtBirth: 'Female', emiratesId: '784-1988-1234567-1', ecName: 'Ahmed Al Mansoori', ecRelationship: 'Spouse', ecPhone: '050 000 0459' },
};
const fields: Record<string, (s: string) => [Partial<RegistrationForm>, FormField]> = {
  mobile: s => [{ mobile: s }, 'mobile'],
  abha: s => [{ abha: s }, 'abha'],
  abhaAddress: s => [{ abhaAddress: s }, 'abhaAddress'],
  aadhaarLast4: s => [{ otherIdType: 'AadhaarLast4', otherIdValue: s }, 'otherIdValue'],
  pin: s => [{ line1: '12 Test Street', city: 'Kochi', region: 'Kerala', postalCode: s }, 'postalCode'],
  emiratesId: s => [{ emiratesId: s }, 'emiratesId'],
  makani: s => [{ line1: 'Villa 1', region: 'DXB', makani: s }, 'makani'],
};
const cases = (Object.keys(bases) as Country[]).flatMap(country => Object.entries(samples[country]).flatMap(([kind, { valid, invalid }]) =>
  [...valid.map(s => [country, kind, s, true] as const), ...invalid.map(s => [country, kind, s, false] as const)]));

describe('the registration form agrees with the server on the shared country samples', () => {
  it.each(cases)('%s %s %s valid: %s', (country, kind, sample, valid) => {
    const [patch, field] = fields[kind]!(sample);
    const r = registrationSchema(country, false, '2026-10-26').safeParse({ ...bases[country], ...patch });
    const flagged = !r.success && r.error.issues.some(i => i.path[0] === field);
    expect(flagged).toBe(!valid);
  });
});
