import { describe, expect, it } from 'vitest';
import { problemOf, searchCodes, verificationKey, type DiagnosisCode } from './problems';

const codes: DiagnosisCode[] = [
  { code: 'E11.9', title: 'Type 2 diabetes mellitus without complications' },
  { code: 'E10.9', title: 'Type 1 diabetes mellitus without complications' },
  { code: 'I10', title: 'Essential (primary) hypertension, high blood pressure' },
  { code: 'O24.4', title: 'Diabetes arising in pregnancy (gestational diabetes)' },
];

describe('searchCodes', () => {
  it('finds by code with or without the dot, in any case, exact code first', () => {
    expect(searchCodes(codes, 'e11.9').map(c => c.code)).toEqual(['E11.9']);
    expect(searchCodes(codes, 'E119').map(c => c.code)).toEqual(['E11.9']);
    expect(searchCodes(codes, 'e1').map(c => c.code)).toEqual(['E10.9', 'E11.9']);
  });

  it('finds by every word of the description', () => {
    expect(searchCodes(codes, 'diabetes').map(c => c.code)).toEqual(['E10.9', 'E11.9', 'O24.4']);
    expect(searchCodes(codes, 'type 2 diabetes').map(c => c.code)).toEqual(['E11.9']);
    expect(searchCodes(codes, 'blood pressure').map(c => c.code)).toEqual(['I10']);
    expect(searchCodes(codes, 'asthma')).toEqual([]);
    expect(searchCodes(codes, '   ')).toEqual([]);
  });

  it('returns in under 300 ms even over a full-size code set', () => {
    const big = Array.from({ length: 15000 }, (_, i) => ({ code: `Q${String(i % 100).padStart(2, '0')}.${i % 10}`, title: `Synthetic condition number ${i}` }));
    const start = performance.now();
    for (const q of ['synthetic condition 149', 'Q4', 'nothing matches this']) searchCodes(big, q);
    expect(performance.now() - start).toBeLessThan(300);
  });
});

describe('labels and errors', () => {
  it('reads a problem verified in this consultation as added today', () => {
    expect(verificationKey({ verification: 'Verified', addedHere: true })).toBe('AddedToday');
    expect(verificationKey({ verification: 'Verified', addedHere: false })).toBe('Verified');
    expect(verificationKey({ verification: 'Provisional', addedHere: true })).toBe('Provisional');
  });

  it('maps server answers to messages', () => {
    expect(['ended', 'duplicate', 'code:unknown', 'description:required', 'onsetOn:future', undefined].map(problemOf))
      .toEqual(['ended', 'duplicate', 'unknown', 'description', 'future', 'failed']);
  });
});
