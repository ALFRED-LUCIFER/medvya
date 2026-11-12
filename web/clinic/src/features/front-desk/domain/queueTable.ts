import type { AppointmentState } from '../../schedule/domain/schedule';
import { sortRows, type SortDirection, type SortState } from '../../table/domain/tableSort';

/** One waiting visit as the nurse queue and the waiting room show it (MDV audit UX-02). Nonclinical only. */
export type QueueRow = { id: string; patientName: string; doctor: string; state: AppointmentState; start: string; since: string; token: string | null };

/** Column definitions are data; the label of each is the i18n key `queue.col.<key>`. */
export const queueColumns = [
  { key: 'patient', sortable: true },
  { key: 'time', sortable: true },
  { key: 'doctor', sortable: true },
  { key: 'status', sortable: false },
  { key: 'waiting', sortable: true },
  { key: 'token', sortable: false },
] as const;
export type QueueColumn = (typeof queueColumns)[number]['key'];
type Sortable = Exclude<QueueColumn, 'token' | 'status'>;

const values: Record<Sortable, (r: QueueRow) => string | number> = {
  patient: r => r.patientName,
  time: r => Date.parse(r.start),
  doctor: r => r.doctor,
  // Ascending = longest wait first, the order the queue is worked in.
  waiting: r => Date.parse(r.since),
};

/** Unsorted rows keep the server's order (longest wait first). */
export const sortQueue = (rows: QueueRow[], sort: SortState<QueueColumn>, locale?: string) =>
  sortRows(rows, sort as SortState<Sortable>, values, locale);

/** What a queue table needs from its controller (useQueueTableController). */
export type QueueTableView = { rows: QueueRow[]; sortOf: (key: QueueColumn) => SortDirection; onSort: (key: QueueColumn) => void };
