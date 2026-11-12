import { describe, expect, it } from 'vitest';
import type { HistoryEntry } from './chart';
import { historySchema, historySections } from './history';

const e = (section: HistoryEntry['section'], text: string, recordedAt: string): HistoryEntry => ({ section, text, author: null, recordedAt });

describe('historySections', () => {
  it('lists the offered sections in order with the newest text current and the rest kept as earlier versions', () => {
    const view = historySections(['PastMedical', 'Social'], [e('Social', 'new', '2026-11-02'), e('Social', 'old', '2026-11-01')]);
    expect(view.map(v => [v.section, v.current?.text ?? null, v.earlier.map(x => x.text)])).toEqual([['PastMedical', null, []], ['Social', 'new', ['old']]]);
  });

  it('still shows a section written at another life stage, but never the blood group (a banner field)', () => {
    const view = historySections(['Function'], [e('Birth', 'term', '2026-01-01'), e('BloodGroup', 'O+', '2026-01-01')]);
    expect(view.map(v => v.section)).toEqual(['Function', 'Birth']);
  });
});

describe('historySchema', () => {
  it('refuses blank text and text over the limit', () => {
    expect(historySchema.safeParse({ text: '   ' }).success).toBe(false);
    expect(historySchema.safeParse({ text: 'x'.repeat(2001) }).success).toBe(false);
    expect(historySchema.safeParse({ text: 'Teacher' }).success).toBe(true);
  });
});
