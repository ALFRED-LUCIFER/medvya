import { useState } from 'react';
import { nextSort, sortDirection, type SortState } from '../../table/domain/tableSort';
import type { StaffMember } from '../domain/staff';
import { sortStaff, type StaffColumn } from '../domain/staffTable';

/** Sort state of the staff table; the rows come from useStaffController. */
export function useStaffTableController(staff: StaffMember[]) {
  const [sort, setSort] = useState<SortState<StaffColumn>>(null);
  return {
    rows: sortStaff(staff, sort),
    sortOf: (key: StaffColumn) => sortDirection(sort, key),
    onSort: (key: StaffColumn) => setSort(nextSort(sort, key)),
  };
}
