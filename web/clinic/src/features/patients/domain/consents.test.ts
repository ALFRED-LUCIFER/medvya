import { describe, expect, it } from 'vitest';
import { changeBody, consentRows, stateOf, type ConsentState } from './consents';

const sms: ConsentState = { type: 'Reminders', channel: 'Sms', granted: false, decidedAt: '2026-11-05T10:00:00Z', policyVersion: '2026-11-draft' };

describe('consents on the record', () => {
  it('finds the decision of a row by type and channel, and none means not recorded', () => {
    expect(stateOf([sms], consentRows[1])).toBe(sms);
    expect(stateOf([sms], consentRows[0])).toBeNull(); // WhatsApp is a different channel
    expect(stateOf([], consentRows[5])).toBeNull();
  });

  it('never offers treatment as a row that can be withdrawn', () => {
    expect(consentRows.some(r => (r.type as string) === 'Treatment')).toBe(false);
  });

  it('sends one decision under the current wording version', () => {
    expect(changeBody(consentRows[1], true)).toEqual({ policyVersion: '2026-11-draft', decisions: [{ type: 'Reminders', channel: 'Sms', granted: true }] });
  });
});
