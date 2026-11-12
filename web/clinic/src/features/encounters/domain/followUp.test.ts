import { describe, expect, it } from 'vitest';
import { followUpProblemOf, nextPeriod } from './followUp';

describe('plan follow-up (MDV-P2-043)', () => {
  it('picking the chosen period again clears it; another one replaces it', () => {
    expect(nextPeriod(null, 'P2W')).toBe('P2W');
    expect(nextPeriod('P2W', 'P1M')).toBe('P1M');
    expect(nextPeriod('P1M', 'P1M')).toBeNull();
  });

  it('reads the server refusals it has words for', () => {
    expect(followUpProblemOf('follow_up_booked')).toBe('follow_up_booked');
    expect(followUpProblemOf('already_waiting')).toBe('already_waiting');
    expect(followUpProblemOf('something_else')).toBe('failed');
    expect(followUpProblemOf(undefined)).toBe('failed');
  });
});
