/** Phones (≤ 767 px, below the md breakpoint) open the schedule as a list: the day grid needs a wider screen. */
export const isPhoneWidth = () => window.matchMedia('(max-width: 767px)').matches;
