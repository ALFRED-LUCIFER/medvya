import { describe, expect, it } from 'vitest';
import { badgeText, notificationTarget, type StaffNotification } from './notifications';

describe('badgeText', () => {
  it('shows the count up to nine and 9+ beyond', () => {
    expect(badgeText(3)).toBe('3');
    expect(badgeText(9)).toBe('9');
    expect(badgeText(10)).toBe('9+');
  });
});

describe('notificationTarget', () => {
  const item = (n: Partial<StaffNotification>): StaffNotification => ({ id: 'n-1', kind: 'MessageFailed', refId: null, detail: null, day: null, createdAt: '2026-11-16T05:00:00Z', read: false, ...n });

  it("opens the doctor's week at the first day for visits to rebook", () => {
    expect(notificationTarget(item({ kind: 'VisitsNeedRebooking', refId: 'd-1', detail: 3, day: '2026-11-18' }))).toBe('/schedule?date=2026-11-18&view=week&doctor=d-1');
  });

  it("falls back to the schedule without a day, and to each kind's own screen otherwise", () => {
    expect(notificationTarget(item({ kind: 'VisitsNeedRebooking', refId: 'd-1' }))).toBe('/schedule');
    expect(notificationTarget(item({ kind: 'MessageFailed', refId: 'a-1' }))).toBe('/front-desk');
  });
});
