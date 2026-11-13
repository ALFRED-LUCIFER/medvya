import { describe, expect, it } from 'vitest';
import { nextSort, pageRows, sortDirection, sortRows, type SortState } from './tableSort';

const values = { name: (r: { name: string | null }) => r.name };

describe('tableSort', () => {
  it('cycles ascending, descending, none, and restarts on another column', () => {
    const a = nextSort<'a' | 'b'>(null, 'a');
    expect(a).toEqual({ key: 'a', direction: 'ascending' });
    const d = nextSort(a, 'a');
    expect(d).toEqual({ key: 'a', direction: 'descending' });
    expect(nextSort(d, 'a')).toBeNull();
    expect(nextSort(d, 'b')).toEqual({ key: 'b', direction: 'ascending' });
  });
  it('maps state to the aria-sort word', () => {
    const s: SortState<'a' | 'b'> = { key: 'a', direction: 'descending' };
    expect([sortDirection(s, 'a'), sortDirection(s, 'b'), sortDirection(null, 'a')]).toEqual(['descending', 'none', 'none']);
  });
  it('sorts a copy, keeps empty values last both ways, and returns unsorted input as is', () => {
    const rows = [{ name: 'b' }, { name: null }, { name: 'a' }];
    expect(sortRows(rows, null, values)).toBe(rows);
    expect(sortRows(rows, { key: 'name', direction: 'ascending' }, values).map(r => r.name)).toEqual(['a', 'b', null]);
    expect(sortRows(rows, { key: 'name', direction: 'descending' }, values).map(r => r.name)).toEqual(['b', 'a', null]);
    expect(rows.map(r => r.name)).toEqual(['b', null, 'a']);
  });
  it('pages over 50 rows, counts pages from 1 row up, and clamps a page past the end to the last page', () => {
    const rows = Array.from({ length: 120 }, (_, i) => i);
    expect(pageRows(rows, 0)).toMatchObject({ page: 0, pageCount: 3 });
    expect(pageRows(rows, 0).rows).toEqual(rows.slice(0, 50));
    expect(pageRows(rows, 2).rows).toEqual(rows.slice(100));
    expect(pageRows(rows, 9)).toMatchObject({ page: 2, pageCount: 3 });
    expect(pageRows(rows.slice(0, 50), 1)).toMatchObject({ page: 0, pageCount: 1, rows: rows.slice(0, 50) });
    expect(pageRows([], 0)).toEqual({ rows: [], page: 0, pageCount: 1 });
  });
});
