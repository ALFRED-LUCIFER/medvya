import type { cast } from './cast';

// MDV-P5-062: on any day, booked patients are still to come, arrivals already happened, and ages match dates of birth.
it.each(['2026-11-14T06:30:00Z', '2027-03-12T09:00:00Z', '2030-02-28T12:00:00Z'])('keeps the day and ages true on %s', today => {
  jest.useFakeTimers({ now: new Date(today) });
  jest.isolateModules(() => {
    const { cast: c } = require('./cast') as { cast: typeof cast };
    const now = new Date().toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit', hourCycle: 'h23', timeZone: 'Asia/Kolkata' });
    for (const q of c.today.queue) {
      if (q.status === 'booked') expect(q.at > now).toBe(true);
      else expect(q.at < now).toBe(true);
    }
    for (const r of Object.values(c.records)) {
      const born = new Date(`${r.dateOfBirth}T00:00:00Z`);
      const age = new Date().getUTCFullYear() - born.getUTCFullYear() - (new Date().toISOString().slice(5, 10) < r.dateOfBirth.slice(5, 10) ? 1 : 0);
      expect(age).toBe(r.age);
    }
  });
  jest.useRealTimers();
});
