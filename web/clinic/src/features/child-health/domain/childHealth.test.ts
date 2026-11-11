import { describe, expect, it } from 'vitest';
import { bandState, doseBody, doseSchema, emptyDose, openDoses, problemOf, type Band, type Dose } from './childHealth';

const dose = (vaccine: Dose['vaccine'], n: number, state: Dose['state']): Dose =>
  ({ vaccine, dose: n, state, dueFrom: '2026-10-19', overdueFrom: '2026-11-16', givenOn: null, batch: null, by: null, deferReason: null, deferredOn: null });

describe('child health (MDV-P2-036)', () => {
  it('reads a band as its most urgent dose', () => {
    const band = (...states: Dose['state'][]): Band => ({ key: 'w6', doses: states.map((s, i) => dose('DTP', i + 1, s)) });
    expect(bandState(band('Given', 'Given'))).toBe('Given');
    expect(bandState(band('Given', 'Upcoming'))).toBe('Upcoming');
    expect(bandState(band('Upcoming', 'DueNow', 'Given'))).toBe('DueNow');
    expect(bandState(band('DueNow', 'Overdue'))).toBe('Overdue');
  });

  it('offers only the doses not yet given', () => {
    expect(openDoses([{ key: 'birth', doses: [dose('BCG', 1, 'Given'), dose('OPV', 0, 'Overdue')] }])).toEqual([{ key: 'OPV|0', vaccine: 'OPV', dose: 0 }]);
  });

  it('needs a day for a given dose and a reason for a deferral, and sends blanks as null', () => {
    expect(doseSchema.safeParse({ ...emptyDose, dose: 'BCG|1' }).success).toBe(false);
    expect(doseSchema.safeParse({ ...emptyDose, dose: 'BCG|1', givenOn: '2026-10-19' }).success).toBe(true);
    expect(doseSchema.safeParse({ ...emptyDose, dose: 'DTP|1', outcome: 'Deferred', reason: '   ' }).success).toBe(false);
    expect(doseBody({ ...emptyDose, dose: 'BCG|1', givenOn: '2026-10-19', batch: ' ' }))
      .toEqual({ vaccine: 'BCG', dose: 1, outcome: 'Given', givenOn: '2026-10-19', batch: null, givenBy: null, reason: null });
    expect(doseBody({ ...emptyDose, dose: 'OPV|0', outcome: 'Deferred', givenOn: '2026-10-19', reason: ' Fever ' }))
      .toEqual({ vaccine: 'OPV', dose: 0, outcome: 'Deferred', givenOn: null, batch: null, givenBy: null, reason: 'Fever' });
  });

  it('names each refusal', () => {
    expect([problemOf(409, 'already_given'), problemOf(400, 'reason:required'), problemOf(403, undefined), problemOf(500, undefined)]).toEqual(['already_given', 'invalid', 'refused', 'failed']);
  });
});
