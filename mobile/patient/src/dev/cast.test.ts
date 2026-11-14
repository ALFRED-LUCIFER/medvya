import type { cast } from './cast';

// MDV-P5-062: on any day, the development cast's upcoming items are in the future and past items in the past.
it.each(['2026-11-14T12:00:00Z', '2027-06-30T23:30:00Z', '2030-02-28T00:10:00Z'])('keeps upcoming in the future and past in the past on %s', today => {
  jest.useFakeTimers({ now: new Date(today) });
  jest.isolateModules(() => {
    const { cast: c } = require('./cast') as { cast: typeof cast };
    const now = Date.now();
    const ms = (iso: string) => new Date(iso.length === 10 ? `${iso}T23:59:59Z` : iso).getTime();
    const video = c.home.tasks.find(t => t.kind === 'video');
    const future = [c.home.nextVisit!.at, video && 'at' in video ? video.at : '', ...c.visits.filter(v => v.status === 'booked').map(v => v.at),
      ...c.people.flatMap(p => (p.proxyUntil ? [p.proxyUntil] : []))];
    const past = [...c.visits.filter(v => v.status !== 'booked').map(v => v.at), ...c.records.visits.map(r => r.date),
      ...c.records.results.map(r => r.date), ...c.messages.map(m => m.at)];
    for (const iso of future) expect(ms(iso)).toBeGreaterThan(now);
    for (const iso of past) expect(ms(iso)).toBeLessThan(now);
  });
  jest.useRealTimers();
});
