import { describe, expect, it } from 'vitest';
import en from '../../../../../../packages/i18n/messages/en.json';
import { actionsFor, approveBlockedByDpa, canDecide, decisionOf, documentKinds, errorKeyOf, sizeLabel, taxCheck, waitDays } from './approvals';

describe('who decides', () => {
  it('lets owner, operations and reviewer decide and support and finance only read', () => {
    expect(['Owner', 'Operations', 'Reviewer'].every(r => canDecide(r as never))).toBe(true);
    expect(['Support', 'Finance'].some(r => canDecide(r as never))).toBe(false);
  });
});

describe('the state machine on screen', () => {
  it('offers approve, ask for changes and reject on a request in review, suspend on an approved one and reinstate on a suspended one', () => {
    expect(actionsFor('Submitted')).toEqual(['approve', 'request-changes', 'reject']);
    expect(actionsFor('Approved')).toEqual(['suspend']);
    expect(actionsFor('Suspended')).toEqual(['reinstate']);
    for (const s of ['Draft', 'NeedsChanges', 'Rejected'] as const) expect(actionsFor(s)).toEqual([]);
  });
  it('names the state each action leads to', () => {
    expect(['approve', 'reinstate', 'request-changes', 'reject', 'suspend'].map(a => decisionOf(a as never))).toEqual(['Approved', 'Approved', 'NeedsChanges', 'Rejected', 'Suspended']);
  });
});

describe('the DPA rule', () => {
  const doc = (kind: string) => ({ id: kind, kind, contentType: 'application/pdf', sizeBytes: 1, uploadedAt: '2026-11-10T00:00:00Z' });
  it('blocks approving a hospital without its signed DPA and nothing else', () => {
    expect(approveBlockedByDpa({ type: 'Hospital', documents: [doc('OwnerId')] })).toBe(true);
    expect(approveBlockedByDpa({ type: 'Hospital', documents: [doc('Dpa')] })).toBe(false);
    expect(approveBlockedByDpa({ type: 'Clinic', documents: [] })).toBe(false);
  });
  it('words the server refusals', () => {
    expect(errorKeyOf({ 'document.Dpa': ['required'] })).toBe('dpa');
    expect(errorKeyOf({ state: ['transition'] })).toBe('state');
    expect(errorKeyOf({ reason: ['required'] })).toBeNull();
  });
});

describe('automatic checks', () => {
  it('checks the tax number format per country', () => {
    expect(taxCheck('IN', '27AAPFU0939F1ZV')).toBe('ok');
    expect(taxCheck('IN', '27aapfu0939f1zv')).toBe('ok');
    expect(taxCheck('IN', '99AAPFU0939F1ZV')).toBe('format'); // no such state code
    expect(taxCheck('IN', '12345')).toBe('format');
    expect(taxCheck('AE', '100123456700003')).toBe('ok');
    expect(taxCheck('AE', '10012345')).toBe('format');
    expect(taxCheck('IN', null)).toBe('missing');
  });
  it('shows sizes and waits in plain units', () => {
    expect(sizeLabel(100)).toBe('1 KB');
    expect(sizeLabel(3 * 1024 * 1024)).toBe('3.0 MB');
    expect(waitDays('2026-11-07T00:00:00Z', new Date('2026-11-10T12:00:00Z'))).toBe(3);
    expect(waitDays(null, new Date())).toBe(0);
  });
});

describe('words', () => {
  it('has a name in the catalogue for every document kind', () => {
    for (const k of documentKinds) expect(en).toHaveProperty([`platform.approvals.doc.${k}`]);
  });
});
