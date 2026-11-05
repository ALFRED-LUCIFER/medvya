import { describe, expect, it } from 'vitest';
import { forgetKey, type HeldKey, keyFor } from './idempotencyKey';

describe('keyFor', () => {
  it('reuses the key for the same request, so a retry or second tap is one submission', () => {
    const held: HeldKey = { current: null };
    expect(keyFor(held, { a: 1 })).toBe(keyFor(held, { a: 1 }));
  });

  it('issues a new key when the request was edited, and after the answer arrived', () => {
    const held: HeldKey = { current: null };
    const first = keyFor(held, { a: 1 });
    expect(keyFor(held, { a: 2 })).not.toBe(first);
    const second = keyFor(held, { a: 2 });
    forgetKey(held);
    expect(keyFor(held, { a: 2 })).not.toBe(second);
  });

  it('makes keys the server accepts (8 to 64 letters, digits, - or _)', () => {
    expect(keyFor({ current: null }, {})).toMatch(/^[A-Za-z0-9_-]{8,64}$/);
  });
});
