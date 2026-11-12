import { expect, test } from '@playwright/test';

// MDV audit UX-12: the request budget of the Settings Hours tab (one of the slowest pages on Azure dev). Opened cold over throttled 4G it reads
// each thing it shows once, all three at the same time (no waterfall), and opening it again within a minute reads nothing: every read is
// audited, so a reread costs a round trip and an audit row. Same method as schedule-performance.spec.ts: the production build, Lighthouse's
// throttled 4G, and mocked answers that wait the round trip plus half the 300 ms read p95 budget.
// shortcut: no time budget yet. The first render is logged: about 2.6 s here, of which about 300 ms is React holding back the lazily loaded
// route after its loading message (Suspense reveal throttling); the real pilot-tier timings need a run against Azure (see issue #1124).
test.use({ baseURL: 'http://localhost:4174', timezoneId: 'Asia/Kolkata' });

const rttMs = 150, serverMs = 150;
const admin = {
  user: { id: 'u-lakshmi', displayName: 'K. Lakshmi (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic (synthetic)', facilityId: 'f-1', facilityName: 'Adyar (synthetic)', role: 'PracticeAdmin' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', country: 'IN', role: 'PracticeAdmin', flags: [], capabilities: { StaffRolesIntegrations: 'Config' } },
};
const doctors = ['Dr. Arjun Rao (synthetic)', 'Dr. S. Menon (synthetic)', 'Dr. Omar Haddad (synthetic)'].map((name, i) => ({
  calendarId: `d-${i + 1}`, calendarKind: 'Doctor', name, slotMinutes: 20, breaks: [], visitLengths: [], appointmentsOutsideHours: [],
  hours: ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday'].map(weekday => ({ weekday, start: '09:00:00', end: '13:00:00', room: null })),
}));
const hours = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'].map(weekday => ({ weekday, opens: '09:00:00', closes: '18:00:00' }));

test('the Hours tab reads each thing once, in parallel, and nothing again when reopened', async ({ page }) => {
  const reads: string[] = [];
  const started: number[] = [];
  const answer = (json: unknown) => async (r: Parameters<Parameters<typeof page.route>[1]>[0]) => {
    reads.push(new URL(r.request().url()).pathname);
    started.push(Date.now());
    await new Promise(done => setTimeout(done, rttMs + serverMs));
    await r.fulfill({ json });
  };
  await page.route('**/v1/me/context', answer(admin));
  await page.route('**/v1/settings/locations', answer([{ id: 'f-1', name: 'Adyar (synthetic)', address: null, timeZone: 'Asia/Kolkata' }]));
  await page.route('**/v1/settings/locations/*/hours', answer(hours));
  await page.route('**/v1/schedule/templates*', answer(doctors));

  const cdp = await page.context().newCDPSession(page);
  await cdp.send('Network.enable');
  await cdp.send('Network.emulateNetworkConditions', { offline: false, latency: rttMs, downloadThroughput: (1.6 * 1024 * 1024) / 8, uploadThroughput: (750 * 1024) / 8 });
  await cdp.send('Network.setCacheDisabled', { cacheDisabled: true });

  await page.goto('/app/settings?tab=hours', { waitUntil: 'commit' });
  const rendered = await page.waitForFunction(
    () => document.body.innerText.includes('Dr. Arjun Rao (synthetic)') && document.querySelectorAll('[role="switch"]').length === 7 && performance.now(),
    null, { polling: 'raf', timeout: 10_000 });
  console.log(`settings hours first render over throttled 4G: ${Math.round(await rendered.jsonValue() as number)} ms`);
  expect(reads.filter(p => p !== '/v1/me/context').sort()).toEqual(['/v1/schedule/templates', '/v1/settings/locations', '/v1/settings/locations/f-1/hours']);
  const settingsStarts = started.filter((_, i) => reads[i] !== '/v1/me/context');
  expect(Math.max(...settingsStarts) - Math.min(...settingsStarts)).toBeLessThan(rttMs + serverMs); // asked together, not one after another

  // Another tab and back within the minute: the Hours tab shows what it read, without reading (and auditing) it again.
  const before = reads.length;
  await page.getByRole('tab', { name: 'Holidays' }).click();
  await page.getByRole('tab', { name: 'Hours' }).click();
  await expect(page.getByRole('heading', { name: 'Bookable schedules' })).toBeVisible();
  expect(reads.slice(before).filter(p => p.endsWith('/hours') || p.includes('/schedule/templates'))).toEqual([]);
});
