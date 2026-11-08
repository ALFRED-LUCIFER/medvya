import { describe, expect, it } from 'vitest';
import type { Trend } from '../../patient360/domain/chart';
import { noFilters, parseFilters, timelineQuery, toggleKind, trendSeries } from './timeline';

describe('filters', () => {
  it('combine and turning the last kind off shows all again', () => {
    const both = toggleKind(toggleKind(noFilters, 'Lab'), 'Medicine');
    expect(both.kinds).toEqual(['Lab', 'Medicine']);
    expect(toggleKind(toggleKind(both, 'Lab'), 'Medicine')).toEqual(noFilters);
  });
  it('become a query of codes and a cursor only', () => {
    expect(timelineQuery(noFilters, null)).toBe('');
    expect(timelineQuery({ kinds: ['Lab', 'Medicine'], source: 'Imported' }, '123.abc')).toBe('?kinds=Lab%2CMedicine&source=Imported&before=123.abc');
  });
  it('are read back only when valid', () => {
    expect(parseFilters(JSON.stringify({ kinds: ['Lab', 'Nope'], source: 'Imported' }))).toEqual({ kinds: ['Lab'], source: 'Imported' });
    expect(parseFilters(JSON.stringify({ kinds: 'Lab', source: 'Nope' }))).toEqual(noFilters);
    expect(parseFilters('not json')).toEqual(noFilters);
    expect(parseFilters(null)).toEqual(noFilters);
  });
});

const trend = (code: Trend['code'], values: number[]): Trend =>
  ({ code, unit: code.startsWith('Blood') ? 'mmHg' : '%', points: values.map((value, i) => ({ value, takenAt: `2026-10-0${i + 1}T04:30:00Z`, source: 'ClinicianVerified' as const })) });

describe('trendSeries', () => {
  it('lists readings newest first for the table and oldest first for the chart', () => {
    const s = trendSeries([trend('HbA1c', [8.1, 7.6, 7.4])], 'HbA1c')!;
    expect(s.rows.map(r => r.value)).toEqual(['7.4', '7.6', '8.1']);
    expect(s.points).toEqual([8.1, 7.6, 7.4]);
  });
  it('joins blood pressure as systolic/diastolic by the time of the reading', () => {
    const s = trendSeries([trend('BloodPressureSystolic', [120, 130]), trend('BloodPressureDiastolic', [80, 85])], 'BloodPressure')!;
    expect(s.rows.map(r => r.value)).toEqual(['130/85', '120/80']);
    expect(s.second).toEqual([80, 85]);
  });
  it('is null with no readings', () => expect(trendSeries([], 'Weight')).toBeNull());
});
