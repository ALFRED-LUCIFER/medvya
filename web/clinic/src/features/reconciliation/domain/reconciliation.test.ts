import { describe, expect, it } from 'vitest';
import { canConfirm, draftProblems, needsDetail, type Reconciliation } from './reconciliation';

const view = (over: Partial<Reconciliation>): Reconciliation => ({ encounterId: 'e', patientId: 'p', rows: [], decided: 0, total: 0, disagree: 0, confirmedAt: null, ...over });

describe('medication reconciliation rules', () => {
  it('confirm waits for every row and for nothing already confirmed', () => {
    expect(canConfirm(view({ decided: 2, total: 3 }))).toBe(false);
    expect(canConfirm(view({ decided: 3, total: 3 }))).toBe(true);
    expect(canConfirm(view({ decided: 3, total: 3, confirmedAt: '2026-11-09T10:00:00Z' }))).toBe(false);
  });

  it('stop and change ask why, and change also asks to what', () => {
    expect([needsDetail('Stop'), needsDetail('Change'), needsDetail('Keep'), needsDetail('Add'), needsDetail('Ignore')]).toEqual([true, true, false, false, false]);
    expect(draftProblems('Stop', { newDose: '', reason: ' ' })).toEqual(['reason']);
    expect(draftProblems('Change', { newDose: '', reason: '' })).toEqual(['reason', 'newDose']);
    expect(draftProblems('Change', { newDose: '10 mg', reason: 'x' })).toEqual([]);
    expect(draftProblems('Keep', { newDose: '', reason: '' })).toEqual([]);
  });
});
