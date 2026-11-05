import { describe, expect, it } from 'vitest';
import { includedStarts, localTime, moveRow, planOf, summarise, toggleRow, zonedInstant } from './series';

describe('series review', () => {
  const plan = planOf([
    { index: 0, start: '2026-10-19T04:30:00Z', conflict: null },
    { index: 1, start: '2026-10-26T04:30:00Z', conflict: 'slot_unavailable' },
  ]);

  it('starts with conflicts skipped and lets the desk skip or include any row', () => {
    expect(includedStarts(plan)).toEqual(['2026-10-19T04:30:00Z']);
    expect(includedStarts(toggleRow(plan, 1))).toHaveLength(2);
    expect(includedStarts(toggleRow(plan, 0))).toEqual([]);
  });

  it('moves a conflict to another time that day in the facility zone and includes it', () => {
    const moved = moveRow(plan, 1, '11:00', 'Asia/Kolkata');
    expect(moved[1]).toEqual({ start: '2026-10-26T05:30:00.000Z', conflict: null, include: true, moved: true });
    expect(localTime(moved[1].start, 'Asia/Kolkata')).toBe('11:00');
  });

  it('counts booked and refused occurrences', () => {
    const done = summarise('A', { seriesId: 's', occurrences: [{ start: 'a', appointment: { id: '1' }, conflict: null }, { start: 'b', appointment: null, conflict: 'closed' }] });
    expect(done).toEqual({ name: 'A', booked: 1, refused: [{ start: 'b', conflict: 'closed' }] });
  });
});

describe('zonedInstant', () => {
  it('reads the wall clock in the facility zone (India and UAE have no daylight saving)', () => {
    expect(zonedInstant('2026-10-19', '10:00', 'Asia/Kolkata')).toBe('2026-10-19T04:30:00.000Z');
    expect(zonedInstant('2026-10-19', '10:00', 'Asia/Dubai')).toBe('2026-10-19T06:00:00.000Z');
  });

  it('follows daylight saving in a zone that has it', () => {
    expect(zonedInstant('2026-10-24', '10:00', 'Europe/London')).toBe('2026-10-24T09:00:00.000Z'); // BST
    expect(zonedInstant('2026-10-26', '10:00', 'Europe/London')).toBe('2026-10-26T10:00:00.000Z'); // GMT after the change
  });
});
