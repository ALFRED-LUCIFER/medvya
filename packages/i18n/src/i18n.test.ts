import { describe, expect, it } from 'vitest';
import handoff from '../../../docs/02-design/data/handoff.json';
import { catalogs, makeI18n, translate, type MessageKey } from './index';

describe('i18n', () => {
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

  it('has no keys in other locales that English lacks', () => {
    for (const cat of [catalogs.hi, catalogs.ar])
      for (const k of Object.keys(cat)) if (!k.startsWith('_')) expect(k in catalogs.en, k).toBe(true);
  });

  it('formats per locale and country with Western digits', () => {
    expect(makeI18n('en', 'IN').formatMoney(10000000, 'INR')).toBe('₹1,00,000.00'); // lakh grouping
    expect(makeI18n('ar', 'AE').dir).toBe('rtl');
    expect(makeI18n('ar', 'AE').formatNumber(1234)).toMatch(/1.?234/);
    expect(makeI18n('hi', 'IN').dir).toBe('ltr');
  });
});
