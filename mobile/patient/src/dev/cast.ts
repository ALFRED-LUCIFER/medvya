// Synthetic cast for Development builds only (docs/00-overview/personas.md). Every person here is invented.
// Read only through features/api/services/source.ts, which refuses outside __DEV__.
import type { Home } from '../features/home/domain/home';
import type { Message } from '../features/messages/domain/messages';
import type { Profile } from '../features/profile/domain/profile';
import type { Records } from '../features/records/domain/records';
import type { Clinic, Person } from '../features/session/domain/session';
import type { Visit } from '../features/visits/domain/visits';

// Dates are relative to today so upcoming items stay in the future and past ones in the past (MDV-P5-062).
const DAY = 86_400_000;
/** The UTC date `days` from today, as YYYY-MM-DD. */
const date = (days: number) => new Date(Date.now() + days * DAY).toISOString().slice(0, 10);
/** An instant `days` from today at a fixed UTC time, as ISO. */
const at = (days: number, utcTime: string) => `${date(days)}T${utcTime}Z`;
/** How the clinic writes a date in free text, e.g. 15 Nov 2026. */
const written = (days: number, opts: Intl.DateTimeFormatOptions = { day: '2-digit', month: 'short', year: 'numeric' }) =>
  new Date(Date.now() + days * DAY).toLocaleDateString('en-GB', { ...opts, timeZone: 'UTC' });

const clinic: Clinic = { name: 'Iyer Family Clinic', branch: 'Adyar', phone: '+914400000102', address: 'Iyer Family Clinic, Adyar, Chennai', country: 'IN', timeZone: 'Asia/Kolkata' };

const people: Person[] = [
  { id: 'pt-meera', name: 'Meera Iyer', initials: 'MI', relation: 'self', clinic: 'Iyer Family Clinic', since: '2019', linked: true },
  { id: 'pt-kavya', name: 'Kavya Iyer', initials: 'KI', relation: 'family', clinic: 'Iyer Family Clinic', since: '2021', linked: false, proxyUntil: date(412) },
];

/** Mobile numbers that have records. Synthetic test numbers, never real ones. */
const accounts: { phone: string; people: string[] }[] = [{ phone: '+919000000101', people: ['pt-meera', 'pt-kavya'] }];

const home: Home = {
  nextVisit: { id: 'v-next', at: at(49, '03:50:00'), doctor: 'Dr. Arjun Rao', reason: '3-month diabetes review' },
  tasks: [
    { kind: 'pay', id: 't-pay', amountMinor: 60000, currency: 'INR', invoice: 'INV-3392' },
    { kind: 'video', id: 't-video', at: at(1, '11:30:00'), doctor: 'Dr. Arjun Rao' },
    { kind: 'lab', id: 't-lab', test: 'Urine test', where: 'Metropolis Lab' },
    { kind: 'medicine', id: 't-med', name: 'Metformin', dose: '1000 mg twice daily with food' },
  ],
};

const visits: Visit[] = [
  { id: 'v-next', at: at(49, '03:50:00'), doctor: 'Dr. Arjun Rao', reason: '3-month diabetes review', status: 'booked' },
  { id: 'v-jul', at: at(-125, '04:30:00'), doctor: 'Dr. Arjun Rao', reason: 'Follow-up visit', status: 'completed' },
  { id: 'v-apr', at: at(-220, '05:00:00'), doctor: 'Dr. Arjun Rao', reason: 'Diabetes review', status: 'completed' },
];

const records: Records = {
  visits: [{ id: 'r-jul', date: date(-125), title: 'Follow-up visit', signedBy: 'Dr. Arjun Rao',
    summary: 'Sugar control improving. Low sugars on glimepiride, so it was stopped. Keep walking 30 minutes a day.' }],
  results: [
    { id: 'res-a1c', test: 'HbA1c', value: '7.9 %', date: date(-47), flag: 'high', doctorNote: 'A little higher than we want. We will talk about it at your next visit.' },
    { id: 'res-egfr', test: 'Kidney function (eGFR)', value: '72', date: date(-47), flag: 'normal', doctorNote: 'Normal.' },
  ],
  medicines: [
    { id: 'm-met', name: 'Metformin', dose: '1000 mg twice daily with food', changed: true },
    { id: 'm-tel', name: 'Telmisartan', dose: '40 mg once daily' },
  ],
};

const messages: Message[] = [
  { id: 'msg-1', from: 'me', author: 'Meera Iyer', text: 'Can I take the new metformin dose with food? It upsets my stomach.', at: at(-8, '05:28:00') },
];

const profile: Profile = {
  insurance: `Star Health ••7781 · valid to ${written(140, { month: 'short', year: 'numeric' })}`,
  choices: [
    { key: 'reminders', on: true, detail: 'WhatsApp and SMS' },
    { key: 'sharing', on: true, detail: `Through ABDM · expires ${written(30)}` },
    { key: 'recording', on: true, detail: '' },
    { key: 'research', on: false, detail: '' },
  ],
};

export const cast = { clinic, people, accounts, home, visits, records, messages, profile };
export type Cast = typeof cast;
