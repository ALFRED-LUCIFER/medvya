import { describe, expect, it } from 'vitest';
import { advanceSchema, canSend, evidenceOf, fileInSchema, nextStates, problemOf, referralSchema, toAdvanceBody, toFileInBody, type Referral } from './referrals';

const referral = (state: Referral['state'], extra: Partial<Referral> = {}): Referral => ({
  id: 'r', patientId: 'p', direction: 'Out', provider: 'Dr. S. Menon', urgency: 'Routine', reason: null, sections: [], state, channel: null, sentAt: null, open: state !== 'Closed',
  letterDocumentId: null, history: [], ...extra,
});

describe('referral rules', () => {
  it('offers only forward moves, and Closed for any open referral', () => {
    expect(nextStates('Draft')).toEqual(['Closed']);
    expect(nextStates('Sent')).toEqual(['AppointmentBooked', 'ReportReceived', 'Closed']);
    expect(nextStates('ReportReceived')).toEqual(['Closed']);
    expect(nextStates('Received')).toEqual(['Closed']);
    expect(nextStates('Closed')).toEqual([]);
  });

  it('sends only a draft referral out', () => {
    expect(canSend(referral('Draft'))).toBe(true);
    expect(canSend(referral('Sent'))).toBe(false);
    expect(canSend(referral('Draft', { direction: 'In' }))).toBe(false);
  });

  it('lists the steps that carry a document as the evidence', () => {
    const r = referral('ReportReceived', { history: [{ state: 'Sent', at: 'a', documentId: null, note: null }, { state: 'ReportReceived', at: 'b', documentId: 'd-1', note: null }] });
    expect(evidenceOf(r).map(s => s.documentId)).toEqual(['d-1']);
  });

  it('needs a provider and a reason for a referral, and a provider and a document for a filed letter', () => {
    expect(referralSchema.safeParse({ provider: ' ', urgency: 'Routine', reason: 'x', sections: [] }).success).toBe(false);
    expect(referralSchema.safeParse({ provider: 'Dr. X', urgency: 'Routine', reason: '', sections: [] }).success).toBe(false);
    expect(fileInSchema.safeParse({ provider: 'Dr. X', reason: '', documentId: '' }).success).toBe(false);
    expect(toFileInBody('p-1', fileInSchema.parse({ provider: ' Dr. X ', reason: '', documentId: 'd-1' }))).toEqual({ patientId: 'p-1', provider: 'Dr. X', reason: null, documentId: 'd-1' });
  });

  it('sends no blank note or document with a status move', () => {
    expect(toAdvanceBody(advanceSchema.parse({ state: 'ReportReceived', documentId: '', note: ' ' }))).toEqual({ state: 'ReportReceived', documentId: null, note: null });
  });

  it('reads the server problem as a sentence the screen has', () => {
    expect(problemOf('consent_required')).toBe('consent_required');
    expect(problemOf('documentId:unknown')).toBe('unknown');
    expect(problemOf('surprise')).toBe('failed');
  });
});
