import { Fragment, type ReactNode } from 'react';
import { ltr, useI18n } from '@medvya/i18n';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@medvya/ui';
import { minutesSince } from '../domain/board';
import { isWaiting } from '../domain/overview';
import { queueColumns, type QueueRow, type QueueTableView } from '../domain/queueTable';

type Props<R extends QueueRow> = Omit<QueueTableView, 'rows' | 'sortOf' | 'onSort'> & {
  rows: R[];
  /** Sorting by a column header; without it the rows keep the order given (the desk list keeps the clinic's queue rule, MDV-P2-052). */
  sortOf?: QueueTableView['sortOf'];
  onSort?: QueueTableView['onSort'];
  label: string;
  timeZone: string;
  /** Server-clock milliseconds, for wait times. */
  now: number;
  /** The row's action (open intake, take, move). */
  action: (row: R) => ReactNode;
  /** Shown under the row across the whole width: an inline form or an error. */
  detail?: (row: R) => ReactNode;
  /** Tags under the patient's name (walk-in, registration incomplete). */
  flags?: (row: R) => ReactNode;
};

/** Visits as one table (MDV audit UX-02): nurse queue, waiting room and the desk list share columns, sort and look. Nonclinical only. */
export default function QueueTable<R extends QueueRow>({ label, rows, sortOf, onSort, timeZone, now, action, detail, flags }: Props<R>) {
  const { t, formatTime } = useI18n();
  return (
    // Stacked under 768 px, so a phone reads each visit as labelled lines instead of scrolling sideways.
    <Table label={label} stacked containerClassName="bg-card" className="md:min-w-[640px]">
      <TableHeader>
        <TableRow>
          {queueColumns.map(c => (
            <TableHead key={c.key} scope="col" sort={c.sortable && sortOf ? sortOf(c.key) : undefined} onSort={c.sortable && onSort ? () => onSort(c.key) : undefined}>{t(`queue.col.${c.key}`)}</TableHead>
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
                {/* Long names wrap: nothing is clipped and the table never grows sideways for a name. */}
                <TableHead scope="row" className="font-bold break-words whitespace-normal">
                  {r.patientName || t('board.unknownPatient')}
                  {flags && <span className="mt-1 flex flex-wrap gap-1 font-normal empty:hidden">{flags(r)}</span>}
                </TableHead>
                <TableCell label={t('queue.col.time')} className="font-mono">{formatTime(new Date(r.start), timeZone)}</TableCell>
                <TableCell label={t('queue.col.doctor')} className="break-words">{r.doctor || t('schedule.doctor')}</TableCell>
                <TableCell label={t('queue.col.status')}>{t(`board.column.${r.state as 'CheckedIn'}`)}</TableCell>
                {/* A wait only while the patient waits; a booked or finished visit has none. */}
                <TableCell label={t('queue.col.waiting')} className="tabular-nums">{isWaiting(r.state) && t('queue.minutes', { minutes: minutesSince(r.since, now) })}</TableCell>
                <TableCell label={t('queue.col.token')} className="font-mono">{r.token && ltr(r.token)}</TableCell>
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
