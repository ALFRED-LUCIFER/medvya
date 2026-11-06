import { sortRows, type SortState } from '../../table/domain/tableSort';
import type { StaffMember } from './staff';

/** Column definitions are data; the label for each is the i18n key `staff.col.<key>`. */
export const staffColumns = [
  { key: 'name', sortable: true },
  { key: 'role', sortable: true },
  { key: 'access', sortable: false },
  { key: 'mfa', sortable: false },
  { key: 'status', sortable: false },
] as const;
export type StaffColumn = (typeof staffColumns)[number]['key'];
type Sortable = 'name' | 'role';

// shortcut: role sorts on the stored role key, not the translated label; sort the label if the list is ever paged server-side.
const values: Record<Sortable, (m: StaffMember) => string> = {
  name: m => m.name ?? m.email ?? '',
  role: m => m.customRoleName ?? m.role,
};

export const sortStaff = (rows: StaffMember[], sort: SortState<StaffColumn>) =>
  sortRows(rows, sort as SortState<Sortable>, values);
