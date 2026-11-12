import { describe, expect, it } from 'vitest';
import { canConfirm, draftProblems, emptyDraft, needsDetail, toSteps, type Reconciliation } from './reconciliation';

const view = (over: Partial<Reconciliation>): Reconciliation => ({ encounterId: 'e', patientId: 'p', rows: [], decided: 0, total: 0, disagree: 0, confirmedAt: null, ...over });

describe('medication reconciliation rules', () => {
  it('confirm waits for every row and for nothing already confirmed', () => {
    expect(canConfirm(view({ decided: 2, total: 3 }))).toBe(false);
    expect(canConfirm(view({ decided: 3, total: 3 }))).toBe(true);
    expect(canConfirm(view({ decided: 3, total: 3, confirmedAt: '2026-11-09T10:00:00Z' }))).toBe(false);
  });

  it('stop and change ask why, and change also asks to what', () => {
    expect([needsDetail('Stop'), needsDetail('Change'), needsDetail('Taper'), needsDetail('Keep'), needsDetail('Add'), needsDetail('Ignore')]).toEqual([true, true, true, false, false, false]);
    expect(draftProblems('Stop', { ...emptyDraft, reason: ' ' })).toEqual(['reason']);
    expect(draftProblems('Change', emptyDraft)).toEqual(['reason', 'newDose']);
    expect(draftProblems('Change', { ...emptyDraft, newDose: '10 mg', reason: 'x' })).toEqual([]);
    expect(draftProblems('Keep', emptyDraft)).toEqual([]);
  });

  it('a taper needs at least one step, each a dose for whole days, a year at most', () => {
    const taper = (steps: { dose: string; days: string }[]) => draftProblems('Taper', { ...emptyDraft, reason: 'x', steps });
    expect(taper([{ dose: '', days: '' }])).toEqual(['steps']);
    expect(taper([{ dose: '10 mg', days: '5' }, { dose: '', days: '' }])).toEqual([]); // a blank row is left out
    expect(taper([{ dose: '10 mg', days: '' }])).toEqual(['steps']);
    expect(taper([{ dose: '10 mg', days: '1.5' }])).toEqual(['steps']);
    expect(taper([{ dose: '10 mg', days: '200' }, { dose: '5 mg', days: '166' }])).toEqual(['steps']);
    expect(toSteps({ ...emptyDraft, steps: [{ dose: ' 10 mg ', days: '7' }, { dose: '', days: '' }] })).toEqual([{ dose: '10 mg', days: 7 }]);
  });
});
