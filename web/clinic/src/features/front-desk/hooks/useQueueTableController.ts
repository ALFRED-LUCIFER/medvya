import { useState } from 'react';
import { useI18n } from '@medvya/i18n';
import { nextSort, sortDirection, type SortState } from '../../table/domain/tableSort';
import { sortQueue, type QueueColumn, type QueueRow, type QueueTableView } from '../domain/queueTable';

/** Sort state of a waiting-visits table (nurse queue, waiting room); the rows come from the board or overview controller. */
export function useQueueTableController(rows: QueueRow[]): QueueTableView {
  const { locale } = useI18n();
  const [sort, setSort] = useState<SortState<QueueColumn>>(null);
  return {
    rows: sortQueue(rows, sort, locale),
    sortOf: (key: QueueColumn) => sortDirection(sort, key),
    onSort: (key: QueueColumn) => setSort(nextSort(sort, key)),
  };
}
