// A phone: under 768 px (DESIGN.md's device table) with a touch screen as its main pointer. A desktop zoomed to 200 % is as narrow but keeps
// its mouse, and stays fully usable (WCAG 1.4.4, 1.4.10).
const phone = () => window.matchMedia('(max-width: 767px) and (pointer: coarse)');

export const isPhone = () => phone().matches;

export function onPhoneChange(changed: () => void) {
  const query = phone();
  query.addEventListener('change', changed);
  return () => query.removeEventListener('change', changed);
}
