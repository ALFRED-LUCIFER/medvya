import { describe, expect, it } from 'vitest';
import { dateIn, directionOf, isDeviceTimeZone, isLocale, todayIn } from './index';

describe('dates in a zone', () => {
  it('todayIn and dateIn give the date in the facility zone, not the device zone', () => {
    const instant = new Date('2026-10-19T20:00:00Z'); // already the 20th in India
    expect(todayIn('Asia/Kolkata', instant)).toBe('2026-10-20');
    expect(todayIn('America/New_York', instant)).toBe('2026-10-19');
    expect(dateIn(new Date('2026-10-18T20:00:00Z'), 'Asia/Dubai')).toBe('2026-10-19');
  });

  it('isDeviceTimeZone is true only for the device zone', () => {
    const device = Intl.DateTimeFormat().resolvedOptions().timeZone;
    expect(isDeviceTimeZone(device)).toBe(true);
    expect(isDeviceTimeZone(device === 'Pacific/Kiritimati' ? 'Asia/Dubai' : 'Pacific/Kiritimati')).toBe(false);
  });
});

describe('locale', () => {
  it('accepts only the supported locales; Arabic reads right to left', () => {
    expect([isLocale('en'), isLocale('hi'), isLocale('ar'), isLocale('fr'), isLocale(null)]).toEqual([true, true, true, false, false]);
    expect([directionOf('ar'), directionOf('hi'), directionOf('en')]).toEqual(['rtl', 'ltr', 'ltr']);
  });
});
