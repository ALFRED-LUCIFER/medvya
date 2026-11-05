import { describe, expect, it } from 'vitest';
import { all, defaultFilters, exportBody, listQuery, rangeOf } from './auditLog';

describe('access log filters', () => {
  it('defaults to the last 30 days', () => {
    const f = defaultFilters(new Date(2026, 10, 5, 12));
    expect([f.from, f.to]).toEqual(['2026-10-06', '2026-11-05']);
  });

  it('turns the chosen days into a half-open range in the browser time zone', () => {
    const { from, to } = rangeOf({ ...defaultFilters(), from: '2026-10-01', to: '2026-10-01' });
    expect(new Date(to!).getTime() - new Date(from!).getTime()).toBe(24 * 3600_000);
    expect(new Date(from!).getHours()).toBe(0);
  });

  it('sends only the filters that were set, and never a name', () => {
    const base = { ...defaultFilters(), from: '', to: '' };
    expect(listQuery({ ...base, actor: all }, null)).toBe('');
    expect(listQuery({ ...base, actor: 'u1', action: 'patient.read' }, 'c1')).toBe('actorUserId=u1&action=patient.read&after=c1');
    expect(exportBody({ ...base, patientId: 'p1' })).toMatchObject({ actorUserId: null, action: null, patientId: 'p1' });
  });
});
