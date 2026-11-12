import { Fragment, type ReactNode } from 'react';
import { ltr, useI18n } from '@medvya/i18n';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@medvya/ui';
import { minutesSince } from '../domain/board';
import { queueColumns, type QueueRow, type QueueTableView } from '../domain/queueTable';

type Props = QueueTableView & {
  label: string;
  timeZone: string;
  /** Server-clock milliseconds, for wait times. */
  now: number;
  /** The row's action (open intake, take, move). */
  action: (row: QueueRow) => ReactNode;
  /** Shown under the row across the whole width: an inline form or an error. */
  detail?: (row: QueueRow) => ReactNode;
};

/** Waiting visits as one table (MDV audit UX-02): nurse queue and waiting room share columns, sort and look. Nonclinical only. */
export default function QueueTable({ label, rows, sortOf, onSort, timeZone, now, action, detail }: Props) {
  const { t, formatTime } = useI18n();
  return (
    <Table label={label} containerClassName="bg-card" className="min-w-[640px]">
      <TableHeader>
        <TableRow>
          {queueColumns.map(c => (
            <TableHead key={c.key} scope="col" sort={c.sortable ? sortOf(c.key) : undefined} onSort={c.sortable ? () => onSort(c.key) : undefined}>{t(`queue.col.${c.key}`)}</TableHead>
          ))}
          <TableHead scope="col"><span className="sr-only">{t('queue.col.action')}</span></TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {rows.map(r => {
          const more = detail?.(r);
          return (
            <Fragment key={r.id}>
              <TableRow>
                <TableHead scope="row" className="font-bold">{r.patientName || t('board.unknownPatient')}</TableHead>
                <TableCell className="font-mono">{formatTime(new Date(r.start), timeZone)}</TableCell>
                <TableCell>{r.doctor || t('schedule.doctor')}</TableCell>
                <TableCell>{t(`board.column.${r.state as 'CheckedIn'}`)}</TableCell>
                <TableCell className="tabular-nums">{t('queue.minutes', { minutes: minutesSince(r.since, now) })}</TableCell>
                <TableCell className="font-mono">{r.token && ltr(r.token)}</TableCell>
                <TableCell className="text-end">{action(r)}</TableCell>
              </TableRow>
              {more && <TableRow><TableCell colSpan={queueColumns.length + 1}>{more}</TableCell></TableRow>}
            </Fragment>
          );
        })}
      </TableBody>
    </Table>
  );
}
