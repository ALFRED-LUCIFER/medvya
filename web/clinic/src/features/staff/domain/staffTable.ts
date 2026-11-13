import { canChangeRole, canOffboard, canReactivate, canResend, canRevoke, canSetLocations, offboardBlock, type StaffMember } from './staff';

/** Column definitions are data; the label for each is the i18n key `staff.col.<key>`. */
export const staffColumns = [
  { key: 'name', sortable: true },
  { key: 'role', sortable: true },
  { key: 'access', sortable: false },
  { key: 'mfa', sortable: false },
  { key: 'status', sortable: false },
] as const;

// shortcut: role sorts on the stored role key, not the translated label; sort the label if the list is ever paged server-side.
export const staffSort = {
  name: (m: StaffMember) => m.name ?? m.email ?? '',
  role: (m: StaffMember) => m.customRoleName ?? m.role,
};
export type StaffSortColumn = keyof typeof staffSort;

/** The i18n keys `staff.<action>` (button text) and `staff.<action>Named` (with the person's name). */
export type StaffAction = 'changeRole' | 'locations' | 'reactivate' | 'resend' | 'revoke' | 'offboard';

/**
 * Row actions (DESIGN.md, Tables): one visible, the rest in a menu. Only an action that opens a form may be the visible one, a
 * 32 px button in a compact row; one that acts at once (resend, revoke) or ends access (offboard) is always a 44 px menu item (MDV-P2-054 AC6).
 */
export function staffActions(m: StaffMember, me: string, iAmOwner: boolean, severalLocations: boolean): { visible: StaffAction | null; menu: StaffAction[] } {
  const some = (pairs: [boolean, StaffAction][]) => pairs.filter(([ok]) => ok).map(([, a]) => a);
  const opens = some([[canChangeRole(m, me), 'changeRole'], [canSetLocations(m) && severalLocations, 'locations'], [canReactivate(m), 'reactivate']]);
  const acts = some([[canResend(m), 'resend'], [canRevoke(m), 'revoke'], [canOffboard(m) && !offboardBlock(m, me, iAmOwner), 'offboard']]);
  return { visible: opens[0] ?? null, menu: [...opens.slice(1), ...acts] };
}
