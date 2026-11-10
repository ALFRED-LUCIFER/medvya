import { describe, expect, it } from 'vitest';
import { directionOf, invalidFields, problemOf, toDraft, toSigBody, type RxLine, type SigDraft } from './prescription';

const blank: SigDraft = { dose: '', doseUnit: '', route: '', frequency: '', timing: '', durationDays: '', quantity: '', instructions: '' };

describe('dose builder rules', () => {
  it('accepts a blank draft and a full one, and sends blanks as null and counts as numbers', () => {
    expect(invalidFields(blank)).toEqual([]);
    const full = { dose: '0.5', doseUnit: 'tablet', route: 'oral', frequency: 'BD', timing: 'with_dinner', durationDays: '5', quantity: '10', instructions: ' Check sugar ' };
    expect(invalidFields(full)).toEqual([]);
    expect(toSigBody(full)).toEqual({ dose: '0.5', doseUnit: 'tablet', route: 'oral', frequency: 'BD', timing: 'with_dinner', durationDays: 5, quantity: 10, instructions: 'Check sugar' });
    expect(toSigBody(blank)).toEqual({ dose: null, doseUnit: null, route: null, frequency: null, timing: null, durationDays: null, quantity: null, instructions: null });
  });

  it('names each field the server would refuse', () => {
    for (const dose of ['0', '0.000', '-1', '1,5', '1.2345', '123456', '1e3']) expect(invalidFields({ ...blank, dose })).toEqual(['dose']);
    expect(invalidFields({ ...blank, durationDays: '0', quantity: '10001', route: 'by_ear', instructions: 'x'.repeat(201) })).toEqual(['route', 'durationDays', 'quantity', 'instructions']);
    expect(invalidFields({ ...blank, durationDays: '1.5' })).toEqual(['durationDays']);
  });

  it('starts the draft from the saved line', () => {
    const line = { dose: '1', doseUnit: 'tablet', route: null, frequency: 'OD', timing: null, durationDays: 7, quantity: null, instructions: null } as RxLine;
    expect(toDraft(line)).toEqual({ ...blank, dose: '1', doseUnit: 'tablet', frequency: 'OD', durationDays: '7' });
  });

  it('prints Arabic right to left and reads refusals as messages', () => {
    expect([directionOf('ar'), directionOf('hi'), directionOf('en')]).toEqual(['rtl', 'ltr', 'ltr']);
    expect(problemOf('encounter_ended')).toBe('encounter_ended');
    expect(problemOf('formularyItemId:unknown')).toBe('unknown');
    expect(problemOf(undefined)).toBe('failed');
  });
});
