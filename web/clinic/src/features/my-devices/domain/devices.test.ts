import { describe, expect, it } from 'vitest';
import { canEnd, orderDevices, type DeviceSession } from './devices';

const s = (id: string, lastSeenAt: string, current = false): DeviceSession => ({ id, device: 'Chrome on Windows', createdAt: lastSeenAt, lastSeenAt, current });

describe('my devices', () => {
  it('lists this device first, then the most recently active', () => {
    const list = [s('old', '2026-11-01T05:00:00Z'), s('here', '2026-11-02T05:00:00Z', true), s('new', '2026-11-03T05:00:00Z')];
    expect(orderDevices(list).map(d => d.id)).toEqual(['here', 'new', 'old']);
  });
  it('never offers to end the session in use', () => {
    expect(canEnd(s('a', '2026-11-01T05:00:00Z', true))).toBe(false);
    expect(canEnd(s('b', '2026-11-01T05:00:00Z'))).toBe(true);
  });
});
