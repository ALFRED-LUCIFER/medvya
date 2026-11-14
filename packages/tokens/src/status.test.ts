import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { medicineGlyph, statuses } from './status';

const en = JSON.parse(readFileSync(new URL('../../i18n/messages/en.json', import.meta.url), 'utf8')) as Record<string, string>;
const all = Object.entries(statuses);

describe('status map', () => {
  it('gives every status words that exist and a glyph in each icon set', () => {
    for (const [name, s] of all) {
      expect(en[s.label], name).toBeTruthy();
      expect(s.web, name).toMatch(/^[A-Z][A-Za-z]+$/);
      expect(s.native, name).toMatch(/^[a-z][a-z-]+$/);
    }
  });

  it('never reuses a glyph, so no two statuses look alike', () => {
    expect(new Set(all.map(([, s]) => s.web)).size).toBe(all.length);
    expect(new Set(all.map(([, s]) => s.native)).size).toBe(all.length);
  });

  it('draws an allergy with a glyph no status shares with medicines, and says Allergy', () => {
    expect(all.map(([, s]) => s.web)).not.toContain(medicineGlyph.web);
    expect(all.map(([, s]) => s.native)).not.toContain(medicineGlyph.native);
    expect(en[statuses.allergy.label]).toBe('Allergy: {substance}');
  });
});
