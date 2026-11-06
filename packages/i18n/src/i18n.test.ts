import { beforeAll, describe, expect, it } from 'vitest';
import handoff from '../../../docs/02-design/data/handoff.json';
import { catalogs, loadCatalog, localeFor, ltr, makeI18n, translate, type MessageKey } from './index';

describe('i18n catalogs', () => {
  it('ships English only; Hindi and Arabic load on demand (first paint carries one catalog)', async () => {
    expect(catalogs.en).toBeDefined();
    expect(catalogs.hi).toBeUndefined();
    expect(catalogs.ar).toBeUndefined();
    await loadCatalog('hi');
    expect(Object.keys(catalogs.hi!).length).toBeGreaterThan(100);
  });
});

describe('i18n', () => {
  beforeAll(async () => { await loadCatalog('hi'); await loadCatalog('ar'); });

  it('keeps the Handoff microcopy keys with their exact English text', () => {
    for (const { k, v } of handoff.copy) {
      const key = (k === 'consent.script.en' ? 'consent.script' : k) as MessageKey; // Handoff names the English variant
      const text = translate('en', key);
      expect(v.endsWith('…') ? text.startsWith(v.slice(0, -1)) : text === v, k).toBe(true);
    }
  });

  it('falls back to English and interpolates', () => {
    expect(translate('ar', 'consent.script')).toBe(translate('en', 'consent.script'));
    expect(translate('en', 'note.signBlocked', { n: 2 })).toBe('Sign note (2 unresolved)');
    expect(translate('hi', 'push.generic', { clinic: 'Iyer Family Clinic' })).toContain('Iyer Family Clinic');
  });

  it('words a count by the plural rules of the locale, Arabic dual included (MDV-P1-052)', () => {
    expect([1, 2].map(n => makeI18n('en', 'IN').tCount('board.count', n))).toEqual(['1 visit', '2 visits']);
    expect([1, 2, 3, 11].map(n => makeI18n('ar', 'AE').tCount('board.count', n))).toEqual(['زيارة واحدة', 'زيارتان', '3 زيارات', '11 زيارة']);
  });

  it('has no keys in other locales that English lacks', () => {
    for (const cat of [catalogs.hi!, catalogs.ar!])
      for (const k of Object.keys(cat)) if (!k.startsWith('_')) expect(k in catalogs.en, k).toBe(true);
  });

  it('formats per locale and country with Western digits', () => {
    expect(makeI18n('en', 'IN').formatMoney(10000000, 'INR')).toBe('₹1,00,000.00'); // lakh grouping
    expect(makeI18n('ar', 'AE').dir).toBe('rtl');
    expect(makeI18n('ar', 'AE').formatNumber(1234)).toMatch(/1.?234/);
    expect(makeI18n('hi', 'IN').dir).toBe('ltr');
  });

  it('shows times in the facility zone, whatever the device zone is', () => {
    const at = new Date('2026-10-19T03:30:00Z');
    expect(makeI18n('en', 'IN').formatTime(at, 'Asia/Kolkata')).toBe('09:00');
    expect(makeI18n('ar', 'AE').formatTime(at, 'Asia/Dubai')).toBe('07:30'); // Western digits in Arabic too
    const late = new Date('2026-11-05T10:08:00Z'); // 03:38 pm in Kolkata, 02:08 pm in Dubai
    expect(makeI18n('en', 'IN').formatTime(late, 'Asia/Kolkata')).toBe('15:38');
    expect(makeI18n('en', 'IN').formatTime(late, 'Asia/Dubai')).toBe('14:08');
    const nearMidnight = new Date('2026-11-05T19:00:00Z'); // already 6 Nov in Kolkata, still 5 Nov in Dubai
    expect(makeI18n('en', 'IN').formatDate(nearMidnight, 'Asia/Kolkata')).toBe('06 Nov 2026');
    expect(makeI18n('en', 'IN').formatDate(nearMidnight, 'Asia/Dubai')).toBe('05 Nov 2026');
    expect(makeI18n('en', 'IN').formatDay('2026-10-19')).toContain('Monday');
    expect(makeI18n('en', 'IN').formatShortDay('2026-10-19')).toBe('Mon, 19 Oct');
  });

  it('maps a device language to a supported locale, else English', () => {
    expect(localeFor('ar')).toBe('ar');
    expect(localeFor('hi')).toBe('hi');
    expect(localeFor('fr')).toBe('en');
    expect(localeFor(null)).toBe('en');
  });

  it('isolates left-to-right values inside Arabic text', () => {
    expect(ltr('+971')).toBe('\u2066+971\u2069');
    expect(translate('ar', 'mobile.patientSignIn.uae', { code: ltr('+971') })).toContain('\u2066+971\u2069');
  });
});
