/** Phones (≤ 767 px, below the md breakpoint) open the schedule as a list: the day grid needs a wider screen. */
export const isPhoneWidth = () => window.matchMedia('(max-width: 767px)').matches;

/** Opens the print dialog and calls `done` when it closes (printed or cancelled). */
export function printPage(done: () => void) {
  window.addEventListener('afterprint', done, { once: true });
  window.print();
}
