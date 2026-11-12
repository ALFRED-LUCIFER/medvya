import type { RxChecks } from './checks';
import type { Medicine, SigDraft, SigField } from './prescription';

// The Add medicine panel (MDV-P2-039, design `dw.med` / medVals): quick picks mapped onto the existing sig codes (Domain/Prescription.cs, Sig),
// so the line prints and shares exactly as one built in the composer. "1-0-0" and "0-0-1" are the sig's MORNING and NIGHT, which keep the
// timing slot free for "after food".
export const howOften = { '1-0-0': 'MORNING', '0-0-1': 'NIGHT', '1-0-1': 'BD', '1-1-1': 'TDS', sos: 'SOS', weekly: 'WEEKLY' } as const;
export const whenTaken = { before_food: 'before_food', after_food: 'after_food', bedtime: 'bedtime', any: '' } as const;
// shortcut: "ongoing" supplies one month (30 days) and prints no duration, as the design does; a repeat-supply rule comes with refills.
export const howLong = { '3d': 3, '5d': 5, '1m': 30, '3m': 90, ongoing: null } as const;
export type HowOften = keyof typeof howOften;
export type WhenTaken = keyof typeof whenTaken;
export type HowLong = keyof typeof howLong;
export type Choice = { often: HowOften | null; when: WhenTaken | null; long: HowLong | null; instructions: string };

const perDay: Record<HowOften, number> = { '1-0-0': 1, '0-0-1': 1, '1-0-1': 2, '1-1-1': 3, sos: 0, weekly: 0 };
const monthDays = 30;

/** A tablet or capsule is counted one per time the notation says ("1-0-1" is one each time); any other form is left to the doctor. */
export function unitOf(form: string | null | undefined): 'tablet' | 'capsule' | null {
  const f = form?.trim().toLowerCase();
  return f === 'tablet' || f === 'capsule' ? f : null;
}

/** How many to hand out: tablets or capsules counted from how often and how long; "when needed", or a form that is not counted, is one pack. */
export function quantityOf(often: HowOften, long: HowLong, unit: 'tablet' | 'capsule' | null): number | 'pack' {
  if (unit === null || often === 'sos') return 'pack';
  const days = howLong[long] ?? monthDays;
  return often === 'weekly' ? Math.ceil(days / 7) : perDay[often] * days;
}

const timesPerDay: Record<string, number> = { OD: 1, MORNING: 1, NIGHT: 1, BD: 2, TDS: 3, QID: 4 };

/** Units to hand out for a counted form (tablet, capsule): dose x times a day x days, rounded up; nothing when any part is missing or the form is not counted. */
export function quantityFor(d: SigDraft): string {
  const [dose, times, days] = [Number(d.dose), timesPerDay[d.frequency], Number(d.durationDays)];
  return (d.doseUnit === 'tablet' || d.doseUnit === 'capsule') && dose > 0 && times && days > 0 ? String(Math.ceil(dose * times * days)) : '';
}

/**
 * MDV-AUDIT-1094: after one edit in the composer, a tablet or capsule gets its unit and oral route when both are blank, and the quantity follows
 * dose x frequency x days until the doctor types their own (a typed quantity is never overwritten). Other forms are left to the doctor.
 */
export function withDefaults(line: { form: string | null }, before: SigDraft, after: SigDraft, field: SigField): SigDraft {
  if (field === 'quantity') return after;
  const unit = unitOf(line.form);
  const d = { ...after };
  if (unit && field !== 'doseUnit' && field !== 'route' && !d.doseUnit && !d.route) Object.assign(d, { doseUnit: unit, route: 'oral' });
  if (before.quantity === '' || before.quantity === quantityFor(before)) d.quantity = quantityFor(d);
  return d;
}

/** The choices as the sig the server keeps. Unchosen stays blank: nothing is guessed. A pack is no number on the line. */
export function draftOf(medicine: Pick<Medicine, 'form'>, c: Choice): SigDraft {
  const unit = unitOf(medicine.form);
  const days = c.long ? howLong[c.long] : null;
  const quantity = c.often && c.long ? quantityOf(c.often, c.long, unit) : 'pack';
  return {
    dose: unit ? '1' : '', doseUnit: unit ?? '', route: unit ? 'oral' : '',
    frequency: c.often ? howOften[c.often] : '', timing: c.when ? whenTaken[c.when] : '',
    durationDays: days === null ? '' : String(days), quantity: quantity === 'pack' ? '' : String(quantity), instructions: c.instructions,
  };
}

/** Search results as one row per medicine (generic and form); its strengths are the formulary rows that share them. */
export const sameMedicine = (a: Medicine, b: Medicine) => a.generic === b.generic && a.form === b.form;
export const medicinesOf = (results: Medicine[]) => results.filter((m, i) => results.findIndex(o => sameMedicine(o, m)) === i);

/** The checks that concern this line: its own findings, and those about the whole patient (allergies not recorded). */
export function checksFor(checks: RxChecks, itemId: string): RxChecks {
  const alerts = checks.alerts.filter(a => a.itemId === itemId || a.itemId === null);
  const open = alerts.filter(a => a.severity === 'Severe' && !checks.overrides.some(o => o.key === a.key));
  return { ...checks, alerts, notChecked: checks.notChecked.filter(n => n.id === itemId), weightDoses: checks.weightDoses.filter(d => d.itemId === itemId), unresolved: open.length };
}
