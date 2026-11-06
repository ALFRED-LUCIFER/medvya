import { emergencyNumber } from '../../home/domain/home';
import { splitVisits, type Visit } from './visits';

const v = (id: string, at: string, status: Visit['status']): Visit => ({ id, at, status, doctor: 'Dr. Arjun Rao', reason: '' });

it('splits visits into upcoming (soonest first) and past (latest first)', () => {
  const { upcoming, past } = splitVisits([
    v('old', '2026-07-12T04:00:00Z', 'completed'), v('far', '2027-03-01T04:00:00Z', 'booked'),
    v('soon', '2027-01-02T03:50:00Z', 'booked'), v('missed', '2026-10-01T04:00:00Z', 'booked'),
  ], new Date('2026-11-07T00:00:00Z'));
  expect(upcoming.map(x => x.id)).toEqual(['soon', 'far']);
  expect(past.map(x => x.id)).toEqual(['missed', 'old']);
});

it('shows the right emergency number per country', () => {
  expect(emergencyNumber('IN')).toBe('108');
  expect(emergencyNumber('AE')).toBe('998');
});
