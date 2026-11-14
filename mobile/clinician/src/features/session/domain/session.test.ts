import { codeForm, emailForm, LOCK_AFTER_MS, shouldLock, tabsFor, unlockMethod } from './session';

it('gives front desk no clinical tab', () => {
  expect(tabsFor('frontDesk')).not.toContain('consult');
  expect(tabsFor('doctor')).toContain('consult');
  expect(tabsFor('doctor').length).toBeLessThanOrEqual(5);
});

it('locks after 15 minutes in the background, not before', () => {
  expect(shouldLock(null, Date.now())).toBe(false);
  expect(shouldLock(0, LOCK_AFTER_MS - 1)).toBe(false);
  expect(shouldLock(0, LOCK_AFTER_MS)).toBe(true);
});

it('validates the work email and the six-digit code', () => {
  expect(emailForm.safeParse({ email: ' Arjun.Rao@Example.com ' }).data?.email).toBe('arjun.rao@example.com');
  expect(emailForm.safeParse({ email: 'arjun' }).success).toBe(false);
  expect(codeForm.safeParse({ code: '123456' }).success).toBe(true);
  expect(codeForm.safeParse({ code: '12345a' }).success).toBe(false);
});

it('never says Face ID or Touch ID on Android, and names what an iPhone has (MDV-P5-059)', () => {
  expect(unlockMethod('android', { face: true, finger: true })).toBe('android');
  expect(unlockMethod('ios', { face: true, finger: false })).toBe('faceId');
  expect(unlockMethod('ios', { face: false, finger: true })).toBe('touchId');
  expect(unlockMethod('ios', { face: false, finger: false })).toBe('ios');
});
