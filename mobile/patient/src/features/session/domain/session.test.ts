import { maskPhone, phoneForm, shouldLock, LOCK_AFTER_MS, toE164 } from './session';

it('normalises Indian and UAE mobile numbers to E.164', () => {
  expect(toE164('IN', '90000 00101')).toBe('+919000000101');
  expect(toE164('IN', '+91 90000-00101')).toBe('+919000000101');
  expect(toE164('IN', '090000 00101')).toBe('+919000000101');
  expect(toE164('AE', '050 000 0101')).toBe('+971500000101');
  expect(toE164('IN', '50000 00101')).toBeNull(); // Indian mobiles start 6–9
  expect(toE164('IN', '9000000')).toBeNull();
  expect(toE164('AE', '9000000101')).toBeNull();
});

it('rejects an invalid number with a translatable message', () => {
  const r = phoneForm.safeParse({ country: 'IN', number: '123' });
  expect(r.success).toBe(false);
  expect(r.error?.issues[0]?.message).toBe('mobile.patientSignIn.phoneInvalid');
});

it('masks all but the last four digits', () => {
  expect(maskPhone('+919000000101')).toBe('+91 ••••••0101');
  expect(maskPhone('+971500000101')).toBe('+971 ••••••0101');
});

it('locks after 15 minutes away', () => {
  expect(shouldLock(0, LOCK_AFTER_MS - 1)).toBe(false);
  expect(shouldLock(0, LOCK_AFTER_MS)).toBe(true);
});
