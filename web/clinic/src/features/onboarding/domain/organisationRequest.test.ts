import { describe, expect, it } from 'vitest';
import { canSubmit, fileProblem, isEditable, itContactSchema, maxDocumentBytes, missingKinds, requestError, type OrganisationRequest } from './organisationRequest';

const clinic: OrganisationRequest = {
  type: 'Clinic', state: 'Draft', reason: null, itContactName: null, itContactEmail: null, submittedAt: null,
  requiredDocuments: ['RegistrationCertificate', 'TaxRegistration', 'OwnerId'], documents: [],
};
const doc = (kind: OrganisationRequest['requiredDocuments'][number]) => ({ id: kind, kind, contentType: 'application/pdf', sizeBytes: 10, uploadedAt: '2026-11-10T00:00:00Z' });

describe('onboarding request (MDV-P1-062)', () => {
  it('is editable only as a Draft or after Medvya asked for changes', () => {
    expect((['Draft', 'NeedsChanges'] as const).map(isEditable)).toEqual([true, true]);
    expect((['Submitted', 'Approved', 'Rejected', 'Suspended'] as const).map(isEditable)).toEqual([false, false, false, false]);
  });

  it('lists the required documents that have no file yet, and submits only when none is missing', () => {
    expect(missingKinds(clinic)).toEqual(['RegistrationCertificate', 'TaxRegistration', 'OwnerId']);
    const partly = { ...clinic, documents: [doc('OwnerId')] };
    expect(missingKinds(partly)).toEqual(['RegistrationCertificate', 'TaxRegistration']);
    expect(canSubmit(partly)).toBe(false);
    expect(canSubmit({ ...clinic, documents: clinic.requiredDocuments.map(doc) })).toBe(true);
    expect(canSubmit({ ...clinic, state: 'Submitted', documents: clinic.requiredDocuments.map(doc) })).toBe(false);
  });

  it('asks a hospital for its IT contact as well', () => {
    const hospital: OrganisationRequest = { ...clinic, type: 'Hospital', requiredDocuments: ['HospitalRegistration', 'SignatoryLetter', 'Dpa'] };
    const complete = { ...hospital, documents: hospital.requiredDocuments.map(doc) };
    expect(canSubmit(complete)).toBe(false);
    expect(canSubmit({ ...complete, itContactName: 'Sanjay Verma (synthetic)', itContactEmail: 'it@hospital.example' })).toBe(true);
    expect(itContactSchema.safeParse({ name: 'S', email: 'not an address' }).success).toBe(false);
  });

  it('checks a file before upload: PDF, PNG or JPEG, not empty, at most 5 MB', () => {
    expect(fileProblem({ type: 'application/pdf', size: 1000 })).toBeNull();
    expect(fileProblem({ type: 'image/svg+xml', size: 1000 })).toBe('type');
    expect(fileProblem({ type: 'image/png', size: 0 })).toBe('size');
    expect(fileProblem({ type: 'image/png', size: maxDocumentBytes + 1 })).toBe('size');
  });

  it('reads only the server codes it has a message for', () => {
    expect(requestError('file:type')).toBe('file:type');
    expect(requestError('request:locked')).toBe('request:locked');
    expect(requestError('anything else')).toBe('failed');
  });
});
