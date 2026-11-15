import { describe, expect, it } from 'vitest';
import { bodyOf, canChange, needsSignOff, releaseForm, signOffState, type ReleaseForm } from './features';

const form = (over: Partial<ReleaseForm>): ReleaseForm => ({ stage: 'Off', percentage: '', organisations: '', clinicalSignOff: '', reason: 'Pilot clinics asked for it', ...over });
const codes = (f: ReleaseForm) => {
  const r = releaseForm.safeParse(f);
  return r.success ? {} : Object.fromEntries(r.error.issues.map(i => [i.path.join('.'), i.message]));
};
const org = '0193a4f2-7c1e-7d00-8a11-2b3c4d5e6f70';

describe('features', () => {
  it('lets owner and operations change a release; support, finance and reviewer only read', () => {
    expect(['Owner', 'Operations', 'Support', 'Finance', 'Reviewer'].filter(r => canChange(r as never))).toEqual(['Owner', 'Operations']);
  });

  it('asks for a share of 1 to 99 only at Percentage', () => {
    expect(codes(form({ stage: 'Percentage', percentage: '0' }))).toEqual({ percentage: 'percentage' });
    expect(codes(form({ stage: 'Percentage', percentage: '100' }))).toEqual({ percentage: 'percentage' });
    expect(codes(form({ stage: 'Percentage', percentage: ' 25 ' }))).toEqual({});
    expect(codes(form({ stage: 'Everyone', percentage: 'x' }))).toEqual({});
  });

  it('needs a named organisation at Beta and accepts only organisation ids', () => {
    expect(codes(form({ stage: 'Beta' }))).toEqual({ organisations: 'required' });
    expect(codes(form({ stage: 'Beta', organisations: 'Rao Clinic' }))).toEqual({ organisations: 'organisations' });
    expect(codes(form({ stage: 'Beta', organisations: `${org}\n` }))).toEqual({});
  });

  it('always needs a reason', () => {
    expect(codes(form({ reason: 'no' }))).toEqual({ reason: 'reason.short' });
  });

  it('asks for a clinical sign-off only for a clinical feature going to everyone without one', () => {
    expect(needsSignOff({ clinical: true, clinicalSignOff: null }, 'Everyone')).toBe(true);
    expect(needsSignOff({ clinical: true, clinicalSignOff: 'CSO (synthetic), HL-041' }, 'Everyone')).toBe(false);
    expect(needsSignOff({ clinical: true, clinicalSignOff: null }, 'Beta')).toBe(false);
    expect(needsSignOff({ clinical: false, clinicalSignOff: null }, 'Everyone')).toBe(false);
  });

  it('says a clinical feature that reaches everyone without a recorded sign-off is missing one', () => {
    const clinical = { clinical: true, alwaysOn: false, stage: 'Everyone' as const, clinicalSignOff: null };
    expect(signOffState(clinical)).toBe('missing');
    expect(signOffState({ ...clinical, clinicalSignOff: 'CSO (synthetic), HL-041' })).toBe('recorded');
    expect(signOffState({ ...clinical, stage: 'Beta' })).toBe('none');
    expect(signOffState({ ...clinical, alwaysOn: true })).toBe('notNeeded');
    expect(signOffState({ ...clinical, clinical: false })).toBe('notNeeded');
  });

  it('sends the share only at Percentage and the organisations as a list', () => {
    expect(bodyOf(form({ stage: 'Beta', organisations: `${org}, ${org.replace('70', '71')}`, clinicalSignOff: ' ' }))).toEqual({
      stage: 'Beta', percentage: null, organisations: [org, org.replace('70', '71')], clinicalSignOff: null, reason: 'Pilot clinics asked for it',
    });
    expect(bodyOf(form({ stage: 'Percentage', percentage: '25' })).percentage).toBe(25);
  });
});
