import { describe, expect, it } from 'vitest';
import { parseResume, returnPath } from './stepUp';

describe('step-up return (MDV-P2-047)', () => {
  it('accepts only the listed words', () => {
    expect(parseResume('authorise')).toBe('authorise');
    expect(parseResume('sign')).toBe('sign');
    for (const v of [null, '', 'Authorise', 'authorise ', 'orders', 'meera', '/consult/x', 'authorise&x=1']) expect(parseResume(v)).toBeNull();
  });

  it('adds the word to the path, or nothing', () => {
    expect(returnPath('/consult/e-1', 'authorise')).toBe('/consult/e-1?resume=authorise');
    expect(returnPath('/consult/e-1')).toBe('/consult/e-1');
  });
});
