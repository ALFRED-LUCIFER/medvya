import { describe, expect, it } from 'vitest';
import { codeSchema, openProblem, shareProblem, shareSchema, toShareBody } from './share';

describe('sharing a prescription (MDV-P2-020)', () => {
  it('sends the patient channel, or the pharmacy and its email, never both', () => {
    expect(toShareBody({ target: 'PatientSms', pharmacyName: 'left over', pharmacyEmail: '' })).toEqual({ recipient: 'Patient', channel: 'Sms' });
    expect(toShareBody({ target: 'Pharmacy', pharmacyName: ' Apollo (synthetic) ', pharmacyEmail: 'orders@pharmacy.example.test' }))
      .toEqual({ recipient: 'Pharmacy', pharmacyName: 'Apollo (synthetic)', pharmacyEmail: 'orders@pharmacy.example.test' });
  });

  it('needs a pharmacy name and email only for a pharmacy', () => {
    expect(shareSchema.safeParse({ target: 'PatientEmail', pharmacyName: '', pharmacyEmail: '' }).success).toBe(true);
    expect(shareSchema.safeParse({ target: 'Pharmacy', pharmacyName: '', pharmacyEmail: 'not an email' }).success).toBe(false);
  });

  it('words each refusal and treats an unknown or expired link alike', () => {
    expect(shareProblem('no_mobile')).toBe('no_mobile');
    expect(shareProblem('pharmacyEmail:invalid')).toBe('failed');
    expect(openProblem(404, undefined)).toBe('unknown');
    expect(openProblem(410, 'replaced')).toBe('replaced');
    expect(openProblem(503, 'send_failed')).toBe('failed');
  });

  it('takes a six-digit code only', () => {
    expect(codeSchema.safeParse({ code: ' 123456 ' }).success).toBe(true);
    expect(codeSchema.safeParse({ code: '12345' }).success).toBe(false);
  });
});
