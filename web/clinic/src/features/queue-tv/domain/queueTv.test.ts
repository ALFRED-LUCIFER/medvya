import { describe, expect, it } from 'vitest';
import { displaySchema, newlyCalled, pairingToken, type QueueTv } from './queueTv';

const token = 'A'.repeat(43) + '_-' + 'b'.repeat(19);
const tv = (...now: string[]): QueueTv => ({ now: now.map(t => ({ token: t, room: 'Room 3', doctor: 'Dr. Arjun Rao (synthetic)' })), next: [] });

describe('queue TV', () => {
  it('takes a display token only from a well-formed fragment', () => {
    expect(pairingToken(`#${token}`)).toBe(token);
    expect(pairingToken(token)).toBe(token);
    for (const bad of ['', '#', `#${token}x`, `#${token.slice(1)}`, `#${token.slice(1)}=`, '#Meera Iyer']) expect(pairingToken(bad)).toBeNull();
  });

  it('chimes only for tokens newly called, never for what was already on screen', () => {
    expect(newlyCalled(undefined, tv('A-01'))).toEqual([]);
    expect(newlyCalled(tv('A-01'), tv('A-01'))).toEqual([]);
    expect(newlyCalled(tv('A-01'), tv('A-02', 'B-01'))).toEqual(['A-02', 'B-01']);
  });

  it('a display needs a short name', () => {
    expect(displaySchema.safeParse({ name: '  Waiting room TV ' }).success).toBe(true);
    expect(displaySchema.safeParse({ name: '   ' }).success).toBe(false);
    expect(displaySchema.safeParse({ name: 'x'.repeat(61) }).success).toBe(false);
  });
});
