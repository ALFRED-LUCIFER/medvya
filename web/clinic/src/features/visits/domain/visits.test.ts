import { describe, expect, it } from 'vitest';
import { visitStatus } from './visits';

describe('visitStatus', () => {
  it('a signed note wins over an ended or open visit', () => {
    expect(visitStatus({ status: 'Ended', noteSigned: true })).toBe('signed');
    expect(visitStatus({ status: 'Active', noteSigned: true })).toBe('signed');
  });
  it('an unsigned visit is open while active and unsigned once ended', () => {
    expect(visitStatus({ status: 'Active', noteSigned: false })).toBe('open');
    expect(visitStatus({ status: 'Ended', noteSigned: false })).toBe('unsigned');
  });
});
