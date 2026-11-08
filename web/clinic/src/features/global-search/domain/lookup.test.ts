import { describe, expect, it } from 'vitest';
import { isShortcut, moveActive, searchableText } from './lookup';

const key = (k: string, mods: Partial<Record<'metaKey' | 'ctrlKey' | 'altKey' | 'shiftKey', boolean>> = {}) =>
  ({ key: k, metaKey: false, ctrlKey: false, altKey: false, shiftKey: false, ...mods });

describe('isShortcut', () => {
  it('is Cmd+K or Ctrl+K and nothing else', () => {
    expect(isShortcut(key('k', { metaKey: true }))).toBe(true);
    expect(isShortcut(key('K', { ctrlKey: true }))).toBe(true);
    expect(isShortcut(key('k'))).toBe(false);
    expect(isShortcut(key('j', { metaKey: true }))).toBe(false);
    expect(isShortcut(key('k', { ctrlKey: true, shiftKey: true }))).toBe(false);
  });
});

describe('moveActive', () => {
  it('wraps at both ends and has no row when the list is empty', () => {
    expect(moveActive(-1, 'ArrowDown', 3)).toBe(0);
    expect(moveActive(2, 'ArrowDown', 3)).toBe(0);
    expect(moveActive(0, 'ArrowUp', 3)).toBe(2);
    expect(moveActive(0, 'ArrowDown', 0)).toBe(-1);
  });
});

describe('searchableText', () => {
  it('waits for two characters and trims', () => {
    expect(searchableText(' m ')).toBeNull();
    expect(searchableText(' me ')).toBe('me');
  });
});
