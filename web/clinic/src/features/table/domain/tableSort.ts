// Pure sort state shared by every sortable table. Directions use the aria-sort words so the view passes them straight to <TableHead sort>.
export type SortDirection = 'ascending' | 'descending' | 'none';
export type SortState<K extends string> = { key: K; direction: 'ascending' | 'descending' } | null;

/** Click cycle on one column: ascending, descending, none. A different column starts at ascending. */
export const nextSort = <K extends string>(current: SortState<K>, key: K): SortState<K> =>
  current?.key !== key ? { key, direction: 'ascending' } : current.direction === 'ascending' ? { key, direction: 'descending' } : null;

export const sortDirection = <K extends string>(current: SortState<K>, key: K): SortDirection => (current?.key === key ? current.direction : 'none');

/** Stable sort on a copy; `values` gives each column's comparable value. Empty values sort last either way. */
export function sortRows<T, K extends string>(rows: T[], current: SortState<K>, values: Record<K, (row: T) => string | number | null>, locale?: string): T[] {
  if (!current) return rows;
  const get = values[current.key];
  const sign = current.direction === 'ascending' ? 1 : -1;
  return [...rows].sort((a, b) => {
    const [x, y] = [get(a), get(b)];
    if (x === null || y === null) return x === y ? 0 : x === null ? 1 : -1;
    return sign * (typeof x === 'number' && typeof y === 'number' ? x - y : String(x).localeCompare(String(y), locale));
  });
}

/** Over this many rows a table is paged (DESIGN.md, Tables). */
export const PAGE_SIZE = 50;

/** One page of rows. `page` counts from 0 and is clamped, so a shorter list (a search, a removal) never leaves the view on an empty page. */
export function pageRows<T>(rows: T[], page: number, size = PAGE_SIZE): { rows: T[]; page: number; pageCount: number } {
  const pageCount = Math.max(1, Math.ceil(rows.length / size));
  const current = Math.min(Math.max(page, 0), pageCount - 1);
  return { rows: rows.slice(current * size, (current + 1) * size), page: current, pageCount };
}

/** What a table controller gives its view: the rows of the current page in order, the sort per column and its announcement, and paging. */
export type TableView<T, K extends string> = {
  rows: T[];
  page: number;
  pageCount: number;
  onPage: (page: number) => void;
  sortOf: (key: K) => SortDirection;
  onSort: (key: K) => void;
  /** "Sorted by Name, ascending" for `<Table status>`; empty when unsorted. */
  status: string;
};
