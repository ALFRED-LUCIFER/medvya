import { describe, expect, it } from 'vitest';
import { codeKey, needsAttention, type PatientMessage } from './messages';

const message = (status: PatientMessage['status']): PatientMessage => ({
  id: 'm', kind: 'Confirmation', channel: 'Email', status, attempts: 1, failureCode: null, sendAt: '2026-10-20T04:30:00Z',
});

describe('patient messages', () => {
  it('flags only failed and bounced messages for the desk', () => {
    expect((['Queued', 'Sent', 'Delivered', 'Cancelled', 'Suppressed'] as const).map(s => needsAttention(message(s)))).toEqual([false, false, false, false, false]);
    expect(needsAttention(message('Failed'))).toBe(true);
    expect(needsAttention(message('Bounced'))).toBe(true);
  });

  it('names known reason codes and calls the rest generic', () => {
    expect(codeKey(null)).toBeNull();
    expect(codeKey('no_consent')).toBe('no_consent');
    expect(codeKey('provider_smtpexception')).toBe('other');
  });
});
