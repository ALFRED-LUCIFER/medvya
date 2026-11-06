// Synthetic cast for Development builds only (docs/00-overview/personas.md). Every person here is invented.
// Read only through features/api/services/source.ts, which refuses outside __DEV__.
import type { Home } from '../features/home/domain/home';
import type { Message } from '../features/messages/domain/messages';
import type { Profile } from '../features/profile/domain/profile';
import type { Records } from '../features/records/domain/records';
import type { Clinic, Person } from '../features/session/domain/session';
import type { Visit } from '../features/visits/domain/visits';

const clinic: Clinic = { name: 'Iyer Family Clinic', branch: 'Adyar', phone: '+914400000102', address: 'Iyer Family Clinic, Adyar, Chennai', country: 'IN', timeZone: 'Asia/Kolkata' };

const people: Person[] = [
  { id: 'pt-meera', name: 'Meera Iyer', initials: 'MI', relation: 'self', clinic: 'Iyer Family Clinic', since: '2019', linked: true },
  { id: 'pt-kavya', name: 'Kavya Iyer', initials: 'KI', relation: 'family', clinic: 'Iyer Family Clinic', since: '2021', linked: false, proxyUntil: '2027-12-31' },
];

/** Mobile numbers that have records. Synthetic test numbers, never real ones. */
const accounts: { phone: string; people: string[] }[] = [{ phone: '+919000000101', people: ['pt-meera', 'pt-kavya'] }];

const home: Home = {
  nextVisit: { id: 'v-next', at: '2027-01-02T03:50:00Z', doctor: 'Dr. Arjun Rao', reason: '3-month diabetes review' },
  tasks: [
    { kind: 'pay', id: 't-pay', amountMinor: 60000, currency: 'INR', invoice: 'INV-3392' },
    { kind: 'video', id: 't-video', at: '2026-11-13T11:30:00Z', doctor: 'Dr. Arjun Rao' },
    { kind: 'lab', id: 't-lab', test: 'Urine test', where: 'Metropolis Lab' },
    { kind: 'medicine', id: 't-med', name: 'Metformin', dose: '1000 mg twice daily with food' },
  ],
};

const visits: Visit[] = [
  { id: 'v-next', at: '2027-01-02T03:50:00Z', doctor: 'Dr. Arjun Rao', reason: '3-month diabetes review', status: 'booked' },
  { id: 'v-jul', at: '2026-07-12T04:30:00Z', doctor: 'Dr. Arjun Rao', reason: 'Follow-up visit', status: 'completed' },
  { id: 'v-apr', at: '2026-04-08T05:00:00Z', doctor: 'Dr. Arjun Rao', reason: 'Diabetes review', status: 'completed' },
];

const records: Records = {
  visits: [{ id: 'r-jul', date: '2026-07-12', title: 'Follow-up visit', signedBy: 'Dr. Arjun Rao',
    summary: 'Sugar control improving. Low sugars on glimepiride, so it was stopped. Keep walking 30 minutes a day.' }],
  results: [
    { id: 'res-a1c', test: 'HbA1c', value: '7.9 %', date: '2026-09-28', flag: 'high', doctorNote: 'A little higher than we want. We will talk about it at your next visit.' },
    { id: 'res-egfr', test: 'Kidney function (eGFR)', value: '72', date: '2026-09-28', flag: 'normal', doctorNote: 'Normal.' },
  ],
  medicines: [
    { id: 'm-met', name: 'Metformin', dose: '1000 mg twice daily with food', changed: true },
    { id: 'm-tel', name: 'Telmisartan', dose: '40 mg once daily' },
  ],
};

const messages: Message[] = [
  { id: 'msg-1', from: 'me', author: 'Meera Iyer', text: 'Can I take the new metformin dose with food? It upsets my stomach.', at: '2026-11-06T05:28:00Z' },
];

const profile: Profile = {
  insurance: 'Star Health ••7781 · valid to Mar 2027',
  choices: [
    { key: 'reminders', on: true, detail: 'WhatsApp and SMS' },
    { key: 'sharing', on: true, detail: 'Through ABDM · expires 15 Nov 2026' },
    { key: 'recording', on: true, detail: '' },
    { key: 'research', on: false, detail: '' },
  ],
};

export const cast = { clinic, people, accounts, home, visits, records, messages, profile };
export type Cast = typeof cast;
