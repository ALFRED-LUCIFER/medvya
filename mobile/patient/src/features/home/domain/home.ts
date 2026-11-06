import type { Country } from '@medvya/i18n';

export type Task =
  | { kind: 'pay'; id: string; amountMinor: number; currency: 'INR' | 'AED'; invoice: string }
  | { kind: 'video'; id: string; at: string; doctor: string }
  | { kind: 'lab'; id: string; test: string; where: string }
  | { kind: 'medicine'; id: string; name: string; dose: string };
export type NextVisit = { id: string; at: string; doctor: string; reason: string };
export type Home = { nextVisit: NextVisit | null; tasks: Task[] };

/** The emergency number shown on Home: 108 in India, 998 (ambulance) in the UAE. */
export const emergencyNumber = (c: Country) => (c === 'AE' ? '998' : '108');
