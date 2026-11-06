import { describe, expect, it } from 'vitest';
import { badgeText } from './notifications';

describe('badgeText', () => {
  it('shows the count up to nine and 9+ beyond', () => {
    expect(badgeText(3)).toBe('3');
    expect(badgeText(9)).toBe('9');
    expect(badgeText(10)).toBe('9+');
  });
});
