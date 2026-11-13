import { useState } from 'react';
import { useI18n } from '@medvya/i18n';
import { nextSort, pageRows, sortDirection, sortRows, type SortState, type TableView } from '../domain/tableSort';

/**
 * Sort and paging state of one table (AGENTS.md section 4). `values` gives each sortable column's comparable value; `label` words a
 * column for the sort announcement. Feed `rows` to the body, `status` to `<Table status>` and `page`, `pageCount`, `onPage` to
 * `<TablePagination>`. A sort goes back to the first page.
 */
export function useTableController<T, K extends string>(rows: T[], values: Record<K, (row: T) => string | number | null>, label: (key: K) => string): TableView<T, K> {
  const { t, locale } = useI18n();
  const [sort, setSort] = useState<SortState<K>>(null);
  const [page, setPage] = useState(0);
  return {
    ...pageRows(sortRows(rows, sort, values, locale), page),
    sortOf: (key: K) => sortDirection(sort, key),
    onSort: (key: K) => { setSort(nextSort(sort, key)); setPage(0); },
    status: sort ? t(`table.sorted.${sort.direction}`, { column: label(sort.key) }) : '',
    onPage: setPage,
  };
}
