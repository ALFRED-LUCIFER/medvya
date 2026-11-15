// @vitest-environment jsdom
import { afterEach, describe, expect, it } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import { useForm } from 'react-hook-form';
import { statusIcons } from '@medvya/ui';
import type { RxChecks, SafetyAlert } from '../domain/checks';
import SafetyChecks from './SafetyChecks';

afterEach(cleanup);

const alert = (key: string, kind: SafetyAlert['kind'], over: Partial<SafetyAlert> = {}): SafetyAlert => ({
  key, kind, severity: 'Severe', itemId: 'i1', medicine: 'Amoxicillin', other: null, otherOnChart: false, code: null, substance: null,
  allergyStatus: null, dailyMicrograms: null, maxDailyMicrograms: null, measure: null, ...over,
});
// Synthetic checks: a penicillin allergy, its cross-sensitivity and a duplicate on the same line.
const checks: RxChecks = {
  ruleSet: 'medvya-curated', ruleSetVersion: '1', allergies: 'Listed', unresolved: 3, checkedAt: '2026-11-14T05:00:00Z', weightDoses: [], notChecked: [], overrides: [],
  alerts: [
    alert('a', 'Allergy', { substance: 'Penicillin', allergyStatus: 'Confirmed', code: 'penicillins' }),
    alert('c', 'CrossSensitivity', { substance: 'Penicillin', allergyStatus: 'Confirmed', code: 'cephalosporins' }),
    alert('d', 'Duplicate', { other: 'Augmentin', code: 'same_medicine' }),
  ],
};

function Checks() {
  const reasonForm = useForm<{ reason: string }>({ defaultValues: { reason: '' } });
  return <SafetyChecks checks={checks} checksLoading={false} checksFailed={false} ended busy={false} overriding={null} reasonForm={reasonForm}
    onOverride={() => {}} onReason={() => {}} />;
}

const glyph = (el: Element) => el.querySelector('svg')!.innerHTML;

describe('SafetyChecks (MDV-P5-057)', () => {
  it('draws an allergy alert with the status map allergy glyph and words, and other alerts with their severity glyph', () => {
    const allergyGlyph = glyph(render(<statusIcons.allergy />).container);
    render(<Checks />);
    const [allergy, cross, duplicate] = screen.getAllByRole('alert');
    expect(allergy!.textContent).toContain('Allergy: Penicillin');
    expect(glyph(allergy!)).toBe(allergyGlyph);
    expect(glyph(cross!)).toBe(allergyGlyph);
    expect(glyph(duplicate!)).not.toBe(allergyGlyph);
  });
});
