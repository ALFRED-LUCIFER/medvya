import { expect, test } from '@playwright/test';

// MDV-P1-023 (NFR 1): the calendar's first render on a clinic PC over throttled 4G stays under 2.5 s for a 3-doctor day.
// Runs against the production build (vite preview), since the dev server's unbundled modules say nothing about real load time.
// Network is Lighthouse's throttled 4G (150 ms RTT, 1.6 Mbps down, 750 kbps up). Mocked API answers skip that throttle, so each
// waits the round trip plus a server time of half the 300 ms read p95 budget: the NFR is page interactive < 2.5 s at p75
// (docs/01-prd/cross-cutting-metrics-process.md), and the endpoints' measured p95 is far below the budget (ScheduleReadPerformanceTests).
// The page's own clock (from navigation start) is read on the first frame where the three columns and their visits are in the DOM.
// shortcut: CI's runner stands in for the clinic PC; the nightly staging run against the real API is MDV-P1-037.
test.use({ baseURL: 'http://localhost:4174', timezoneId: 'Asia/Kolkata' });

const budgetMs = 2_500;
const rttMs = 150, serverMs = 150;
const ist = (hhmm: string) => {
  const [h, m] = hhmm.split(':').map(Number);
  return new Date(Date.parse('2026-10-19T00:00:00Z') + ((h! * 60 + m!) - 330) * 60_000).toISOString();
};

/** Three doctors, 20-minute slots 09:00–17:00, every other slot booked: a full clinic day. */
function day() {
  const doctors = ['Dr. Arjun Rao (synthetic)', 'Dr. S. Menon (synthetic)', 'Dr. Fatima Haddad (synthetic)'];
  return {
    facilityId: 'f-1', timeZone: 'Asia/Kolkata', date: '2026-10-19', start: ist('09:00'), end: ist('17:00'),
    columns: doctors.map((name, d) => {
      const starts = [...Array(24).keys()].map(i => Date.parse(ist('09:00')) + i * 20 * 60_000);
      const span = (t: number) => ({ start: new Date(t).toISOString(), end: new Date(t + 20 * 60_000).toISOString() });
      return {
        calendarId: `d-${d + 1}`, calendarKind: 'Doctor', name, slotMinutes: 20, capacity: 24, unavailable: [],
        hours: [{ start: ist('09:00'), end: ist('17:00'), room: `Room ${d + 1}` }],
        visitLengths: [{ visitType: 'FollowUp', minutes: 20 }, { visitType: 'NewConsultation', minutes: 40 }, { visitType: 'Procedure', minutes: 20 }, { visitType: 'Teleconsult', minutes: 20 }],
        slots: starts.filter((_, i) => i % 2 === 1).map(t => ({ ...span(t), held: false })),
        appointments: starts.filter((_, i) => i % 2 === 0).map((t, i) => ({
          id: `a-${d}-${i}`, reference: `APT-${d}${i}`, patientId: `p-${d}-${i}`, patientName: `Synthetic Patient ${d}-${i}`, visitType: 'FollowUp', state: 'Booked', ...span(t),
        })),
      };
    }),
  };
}

test('the 3-doctor day renders within 2.5 s over throttled 4G', async ({ page }) => {
  const reception = {
    user: { id: 'u-1', displayName: 'A. Joseph' },
    memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role: 'Receptionist' }],
    active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'Receptionist', flags: [], capabilities: { RegisterBookPatients: 'Full', CheckInQueue: 'Full' } },
  };
  const answer = (json: unknown) => async (r: Parameters<Parameters<typeof page.route>[1]>[0]) => {
    await new Promise(done => setTimeout(done, rttMs + serverMs));
    await r.fulfill({ json });
  };
  await page.route('**/v1/me/context', answer(reception));
  await page.route('**/v1/schedule?*', answer(day()));

  const cdp = await page.context().newCDPSession(page);
  await cdp.send('Network.enable');
  await cdp.send('Network.emulateNetworkConditions', { offline: false, latency: rttMs, downloadThroughput: (1.6 * 1024 * 1024) / 8, uploadThroughput: (750 * 1024) / 8 });
  await cdp.send('Network.setCacheDisabled', { cacheDisabled: true }); // a first visit: nothing cached

  await page.goto('/schedule', { waitUntil: 'commit' });
  const rendered = await page.waitForFunction(
    () => ['d-1', 'd-2', 'd-3'].every(d => document.getElementById(`schedule-col-${d}`)) && document.body.innerText.includes('Synthetic Patient 2-11') && performance.now(),
    null, { polling: 'raf', timeout: 10_000 });
  const elapsed = Math.round(await rendered.jsonValue() as number);
  for (const d of ['d-1', 'd-2', 'd-3']) await expect(page.locator(`#schedule-col-${d}`)).toBeVisible();
  console.log(`schedule first render over throttled 4G: ${elapsed} ms (budget ${budgetMs} ms)`);
  expect(elapsed).toBeLessThan(budgetMs);
});
