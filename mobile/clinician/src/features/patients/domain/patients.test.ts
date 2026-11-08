import { matchPatient, type PatientListItem } from './patients';
import { orderQueue, type QueueEntry } from '../../today/domain/today';

const meera: PatientListItem = { id: 'p1', name: 'Meera Iyer', initials: 'MI', age: 54, sex: 'F', phoneLast4: '4471', mrnLast4: '4482' };

it('finds a patient by any name word, last phone digits or last MRN digits', () => {
  expect(matchPatient(meera, 'iye')).toBe(true);
  expect(matchPatient(meera, 'meera i')).toBe(true);
  expect(matchPatient(meera, '4471')).toBe(true);
  expect(matchPatient(meera, '82')).toBe(true);
  expect(matchPatient(meera, 'rao')).toBe(false);
  expect(matchPatient(meera, '9999')).toBe(false);
  expect(matchPatient(meera, '  ')).toBe(true);
});

it('orders the queue: with doctor, next, waiting, then booked by time', () => {
  const e = (token: string, status: QueueEntry['status'], at: string): QueueEntry => ({ token, status, at, patientId: token, name: token, age: 1, sex: 'F', note: '' });
  expect(orderQueue([e('b2', 'booked', '11:30'), e('w', 'waiting', '09:00'), e('b1', 'booked', '11:00'), e('d', 'withDoctor', '10:00')]).map(x => x.token))
    .toEqual(['d', 'w', 'b1', 'b2']);
});
