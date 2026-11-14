/** Phones (≤ 767 px, below the md breakpoint) open the schedule as a list: the day grid needs a wider screen. */
export const isPhoneWidth = () => window.matchMedia('(max-width: 767px)').matches;

/** Whether the device is in this IANA zone; both are resolved first, since the engine may name it by an alias (Asia/Calcutta). */
export const isDeviceTimeZone = (zone: string) => new Intl.DateTimeFormat('en', { timeZone: zone }).resolvedOptions().timeZone === Intl.DateTimeFormat().resolvedOptions().timeZone;

/** Tablets (768-1279 px, md up to lg): an open booking or visit is a drawer over the schedule there. */
const tablet = () => window.matchMedia('(min-width: 768px) and (max-width: 1279px)');
export const isTabletWidth = () => tablet().matches;
export function onTabletWidthChange(changed: () => void) {
  const query = tablet();
  query.addEventListener('change', changed);
  return () => query.removeEventListener('change', changed);
}

/** Opens the print dialog and calls `done` when it closes (printed or cancelled). */
export function printPage(done: () => void) {
  window.addEventListener('afterprint', done, { once: true });
  window.print();
}
