// Synthetic cast for Development builds only (docs/00-overview/personas.md). Every person here is invented.
// Read only through features/api/services/source.ts, which refuses outside __DEV__.
import type { Inbox } from '../features/inbox/domain/inbox';
import type { PatientListItem, PatientRecord } from '../features/patients/domain/patients';
import type { Facility, Staff } from '../features/session/domain/session';
import type { Today } from '../features/today/domain/today';

// Dates and clock times are relative to now so the day's queue and records read true on any day (MDV-P5-062).
const DAY = 86_400_000;
const clinicZone = 'Asia/Kolkata';
/** The clinic's clock `minutes` from now, as HH:mm. */
const clock = (minutes: number) =>
  new Date(Date.now() + minutes * 60_000).toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit', hourCycle: 'h23', timeZone: clinicZone });
/** A date of birth that makes the person `age` today, as YYYY-MM-DD. */
const bornAged = (age: number) => {
  const d = new Date(Date.now() - 60 * DAY);
  d.setUTCFullYear(d.getUTCFullYear() - age);
  return d.toISOString().slice(0, 10);
};
/** A short date `days` from today, e.g. 28 Sep. */
const shortDate = (days: number) => new Date(Date.now() + days * DAY).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', timeZone: clinicZone });

const staff: Staff[] = [
  { id: 's-rao', name: 'Dr. Arjun Rao', initials: 'AR', email: 'arjun.rao@example.com', role: 'doctor' },
  { id: 's-joseph', name: 'A. Joseph', initials: 'AJ', email: 'reception.desk@example.com', role: 'frontDesk' },
];

const facilities: Facility[] = [
  { id: 'f-adyar', name: 'Adyar', city: 'Chennai', opens: '09:00', closes: '20:00', timeZone: 'Asia/Kolkata' },
  { id: 'f-velachery', name: 'Velachery', city: 'Chennai', opens: '10:00', closes: '18:00', timeZone: 'Asia/Kolkata' },
];

const patients: PatientListItem[] = [
  { id: 'p-meera', name: 'Meera Iyer', initials: 'MI', age: 54, sex: 'F', phoneLast4: '4471', mrnLast4: '4482', flag: 'allergy' },
  { id: 'p-joseph', name: 'Joseph Mathew', initials: 'JM', age: 67, sex: 'M', phoneLast4: '2290', mrnLast4: '3107', flag: 'overdue' },
  { id: 'p-kavya', name: 'Kavya Menon', initials: 'KM', age: 31, sex: 'F', phoneLast4: '1182', mrnLast4: '5521', flag: 'new' },
  { id: 'p-lakshmi', name: 'Lakshmi Pillai', initials: 'LP', age: 58, sex: 'F', phoneLast4: '8823', mrnLast4: '2214' },
  { id: 'p-rahul', name: 'Rahul Menon', initials: 'RM', age: 61, sex: 'M', phoneLast4: '6642', mrnLast4: '7730', flag: 'critical' },
  { id: 'p-ananya', name: 'Ananya Sharma', initials: 'AS', age: 29, sex: 'F', phoneLast4: '5016', mrnLast4: '6408' },
];

const meera: PatientRecord = {
  ...patients[0]!,
  dateOfBirth: bornAged(54),
  allergies: [{ substance: 'Penicillin', reaction: 'rash', verified: true }],
  vitals: [
    { label: 'HbA1c', value: '7.9 %', note: shortDate(-47), trend: 'up' },
    { label: 'BP', value: '138/86', note: 'today' },
    { label: 'eGFR', value: '72', note: 'normal' },
  ],
  sinceLastVisit: [
    { text: 'HbA1c rising: 7.9 %', status: 'verified' },
    { text: 'Microalbumin never done', status: 'aiUnreviewed' },
    { text: '“Foot burning” at booking', status: 'aiUnreviewed' },
  ],
  problems: ['Type 2 diabetes', 'Hypertension'],
  medicines: ['Metformin 1000 mg twice daily with food', 'Telmisartan 40 mg once daily'],
};

const records: Record<string, PatientRecord> = Object.fromEntries(patients.map(p => [p.id, p.id === meera.id ? meera : {
  ...p, dateOfBirth: bornAged(p.age), allergies: [], vitals: [], sinceLastVisit: [], problems: [], medicines: [],
}]));

const today: Today = {
  next: {
    patientId: 'p-meera', name: 'Meera Iyer', initials: 'MI', age: 54, sex: 'F', reason: 'Diabetes follow-up', room: 'Room 3',
    flags: [{ kind: 'allergy', label: 'Penicillin' }, { kind: 'result', label: 'HbA1c 7.9 %' }, { kind: 'brief', label: 'Brief · not reviewed' }],
  },
  seen: 6, waiting: 2, toReview: 2,
  queue: [
    { token: 'A-09', patientId: 'p-ananya', name: 'Ananya Sharma', age: 29, sex: 'F', note: clock(30), status: 'booked', at: clock(30), viaApp: true },
    { token: 'A-05', patientId: 'p-kavya', name: 'Kavya Menon', age: 31, sex: 'F', note: 'with doctor', status: 'withDoctor', at: clock(-35) },
    { token: 'A-08', patientId: 'p-joseph', name: 'Joseph Mathew', age: 67, sex: 'M', note: 'waiting 12 min', status: 'waiting', at: clock(-12) },
    { token: 'A-07', patientId: 'p-meera', name: 'Meera Iyer', age: 54, sex: 'F', note: 'Room 3', status: 'next', at: clock(-25), viaApp: true },
  ],
};

const inbox: Inbox = {
  results: [
    { id: 'r1', patient: 'Rahul Menon', test: 'Potassium', value: '6.1 mmol/L', severity: 'critical', acknowledged: false },
    { id: 'r2', patient: 'Meera Iyer', test: 'HbA1c', value: '7.9 %', severity: 'abnormal', acknowledged: false },
  ],
  messages: [{ id: 'm1', patient: 'Meera Iyer', initials: 'MI', text: 'Can I take the new metformin dose with food? It upsets my stomach.', replyBy: clock(180) }],
};

export const cast = { staff, facilities, patients, records, today, inbox };
export type Cast = typeof cast;
