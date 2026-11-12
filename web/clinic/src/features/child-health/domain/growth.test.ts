import { describe, expect, it } from 'vitest';
import { centileWord, emptyGrowth, growthBody, growthChart, growthProblemOf, growthSchema, latestOf, type GrowthSeries } from './growth';

const line = (centile: 3 | 15 | 50 | 85 | 97, at0: number, at60: number) => ({ centile, points: [{ ageDays: 0, value: at0 }, { ageDays: 1826, value: at60 }] });
const series = (points: GrowthSeries['points']): GrowthSeries => ({
  indicator: 'WeightForAge', flags: [], points,
  lines: [line(3, 2.4, 13.7), line(15, 2.8, 15.5), line(50, 3.2, 18.2), line(85, 3.7, 21.3), line(97, 4.2, 24.2)],
});
const pt = (takenOn: string, ageDays: number, value: number, z: number, centile: number) => ({ takenOn, ageDays, value, z, centile });

describe('growth chart', () => {
  it('draws five lines, two bands and the child inside the plot area, with a tick per year', () => {
    const c = growthChart(series([pt('2026-09-07', 0, 3.2, -0.07, 47.2), pt('2026-10-19', 42, 4, -1, 15.9)]))!;
    expect(c.lines.map(l => l.centile)).toEqual([3, 15, 50, 85, 97]);
    expect(c.bands).toHaveLength(2);
    expect(c.child).toHaveLength(2);
    for (const p of c.child) {
      expect(p.x).toBeGreaterThanOrEqual(44);
      expect(p.x).toBeLessThanOrEqual(488);
      expect(p.y).toBeGreaterThanOrEqual(20);
      expect(p.y).toBeLessThanOrEqual(232);
    }
    expect(c.xTicks.map(x => x.years)).toEqual([0, 1, 2, 3, 4, 5]);
    expect(c.childPath.startsWith('M')).toBe(true);
  });

  it('has nothing to draw without lines (a child over five)', () => {
    expect(growthChart({ ...series([]), lines: [] })).toBeNull();
  });

  it('names the latest centile and the trend against the point before', () => {
    expect(latestOf(series([]))).toBeNull();
    expect(latestOf(series([pt('a', 0, 3.2, -0.07, 47.2)]))!.trend).toBe('single');
    expect(latestOf(series([pt('a', 0, 3.2, -0.07, 47.2), pt('b', 42, 4, -1, 15.9)]))!.trend).toBe('down');
    expect(latestOf(series([pt('a', 0, 3.2, -1, 15.9), pt('b', 42, 4, 0.5, 69)]))!.trend).toBe('up');
    expect([centileWord(0.04), centileWord(15.9), centileWord(99.97)]).toEqual([1, 16, 99]);
  });
});

describe('growth form', () => {
  it('needs one measurement and a position for a length; blank stays null', () => {
    expect(growthSchema.safeParse({ ...emptyGrowth, takenOn: '2026-10-19' }).success).toBe(false);
    expect(growthSchema.safeParse({ ...emptyGrowth, takenOn: '2026-10-19', lengthCm: '53.5' }).success).toBe(false);
    expect(growthSchema.safeParse({ ...emptyGrowth, takenOn: '2026-10-19', weightKg: 'abc' }).success).toBe(false);
    expect(growthSchema.safeParse({ ...emptyGrowth, takenOn: '2026-10-19', weightKg: '4,2' }).success).toBe(true);
    expect(growthBody({ ...emptyGrowth, takenOn: '2026-10-19', weightKg: ' 4,2 ', position: 'Lying' }))
      .toEqual({ takenOn: '2026-10-19', weightKg: 4.2, lengthCm: null, position: null, headCm: null });
    expect(growthBody({ takenOn: '2026-10-19', weightKg: '', lengthCm: '53.5', position: 'Lying', headCm: '37' }))
      .toEqual({ takenOn: '2026-10-19', weightKg: null, lengthCm: 53.5, position: 'Lying', headCm: 37 });
    expect([growthProblemOf(409, 'not_a_child'), growthProblemOf(400, undefined), growthProblemOf(403, undefined), growthProblemOf(500, undefined)])
      .toEqual(['not_a_child', 'invalid', 'refused', 'failed']);
  });
});
