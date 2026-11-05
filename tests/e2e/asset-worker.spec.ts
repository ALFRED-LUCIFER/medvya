import { expect, test } from '@playwright/test';

// MDV-P1-049: after one visit the production build's shell reloads with the network off, shows the offline state, and nothing
// from the API is stored. Runs against `vite preview` (the build, base /app/), the only place the worker registers.
const context = {
  user: { id: 'u-joseph', displayName: 'Synthetic Marker Patient' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role: 'Receptionist' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'Receptionist', flags: [],
    capabilities: { RegisterBookPatients: 'Full', CheckInQueue: 'Full', InvoicesPayments: 'Full', Patient360Chart: 'None', ClinicalNotes: 'None' } },
};
test.use({ baseURL: 'http://localhost:4174', serviceWorkers: 'allow' });

for (const locale of ['en', 'ar']) {
  test(`app shell reloads offline with the offline state and no stored patient data (${locale})`, async ({ page, context: browser }) => {
    await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
    // Synthetic session; the display name is a marker that must never reach any browser storage or cache.
    await page.route('**/v1/me/context', r => r.fulfill({ json: context }));
    await page.goto('/app/');
    await page.evaluate(() => navigator.serviceWorker.ready);
    await page.reload(); // now controlled by the worker, so the assets are cached
    await page.waitForLoadState('networkidle');

    await page.unrouteAll(); // offline, the API must fail for real, not be answered by the mock
    await browser.setOffline(true);
    await page.reload();
    await expect(page.locator('#root')).not.toBeEmpty();
    // The offline banner (state.offline.title); the app cannot reach the session, so no name or patient data is shown.
    await expect(page.getByText(locale === 'en' ? 'You are offline' : 'غير متصل', { exact: false }).first()).toBeVisible();
    await expect(page.getByText('Synthetic Marker Patient')).toHaveCount(0);

    const stored = await page.evaluate(async () => {
      const cacheUrls = (await Promise.all((await caches.keys()).map(async k => (await (await caches.open(k)).keys()).map(r => new URL(r.url).pathname)))).flat();
      return { cacheUrls, web: JSON.stringify([{ ...localStorage }, { ...sessionStorage }]), dbs: (await indexedDB.databases()).length };
    });
    expect(stored.cacheUrls.length).toBeGreaterThan(0);
    expect(stored.cacheUrls.every(p => p.startsWith('/app/'))).toBe(true);
    expect(stored.cacheUrls.some(p => p.includes('/v1/'))).toBe(false);
    expect(stored.web).not.toContain('Synthetic Marker');
    expect(stored.dbs).toBe(0);
  });
}

test('API calls still go to the network while the worker is active', async ({ page }) => {
  let hits = 0;
  await page.route('**/v1/**', r => { hits++; return r.fulfill({ json: {} }); });
  await page.goto('/app/');
  await page.evaluate(() => navigator.serviceWorker.ready);
  await page.reload();
  await page.waitForLoadState('networkidle');
  const before = hits;
  await page.evaluate(() => fetch('/v1/me/context'));
  expect(hits).toBe(before + 1);
});
