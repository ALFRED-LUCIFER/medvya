import { describe, expect, it } from 'vitest';
import { doctorSchema, emptyDoctor, errorTarget, signatureProblem, stepFields, toAddBody } from './doctors';

const valid = {
  ...emptyDoctor('f1', { newVisitFee: 50000, followUpFee: 30000 }),
  fullName: 'Dr. Sana Khan (synthetic)', email: 'sana@example.test', specialty: 'Paediatrics', authority: 'TNMC', number: '84213', category: 'Permanent', expiresOn: '2031-12-31',
};

describe('doctor form', () => {
  it('needs an expiry date, a category and a licence number', () => {
    expect(doctorSchema.safeParse(valid).success).toBe(true);
    for (const field of ['expiresOn', 'category', 'number', 'authority', 'email'] as const)
      expect(doctorSchema.safeParse({ ...valid, [field]: '' }).success).toBe(false);
  });

  it('sends fees as minor units and blank optional values as null', () => {
    const body = toAddBody({ ...valid, newVisitFee: '499.5' });
    expect(body).toMatchObject({ newVisitFee: 49950, followUpFee: 30000, qualifications: null, certificateName: null, licenceNumber: '84213' });
  });

  it('prefills the practice fee defaults in whole currency units', () => {
    expect(emptyDoctor('f1', { newVisitFee: 50000, followUpFee: 30000 })).toMatchObject({ newVisitFee: '500', followUpFee: '300' });
  });

  it('puts a server error back under the step and field it belongs to', () => {
    expect(errorTarget('licence.number')).toEqual({ step: 'licence', field: 'number' });
    expect(errorTarget('licence.expiresOn')).toEqual({ step: 'licence', field: 'expiresOn' });
    expect(errorTarget('email')).toEqual({ step: 'details', field: 'email' });
    expect(errorTarget('newVisitFee')).toEqual({ step: 'fees', field: 'newVisitFee' });
    expect(errorTarget('nothing')).toBeNull();
    expect(stepFields.verification).toEqual([]);
  });

  it('accepts only PNG or JPEG signatures within the size limit', () => {
    expect(signatureProblem({ type: 'image/png', size: 1000 })).toBeNull();
    expect(signatureProblem({ type: 'image/svg+xml', size: 1000 })).toBe('type');
    expect(signatureProblem({ type: 'image/jpeg', size: 600 * 1024 })).toBe('size');
  });
});
