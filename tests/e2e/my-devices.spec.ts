import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P0-012: "My devices". CI has no API: the endpoints are mocked in the shapes the server sends; ending a session, its audit and
// "effective on the next request" are covered by the SessionControl integration tests on PostgreSQL.
const facility = '11111111-1111-4111-8111-111111111111';
const receptionist = {
  user: { id: 'u-joseph', displayName: 'A. Joseph (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: facility, facilityName: 'Adyar', role: 'Receptionist' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: facility, role: 'Receptionist', flags: [] as string[], timeZone: 'Asia/Kolkata', capabilities: { RegisterBookPatients: 'Full', CheckInQueue: 'Full' } },
};

async function open(page: Page, locale = 'en') {
  const state = {
    sessions: [
      { id: 'desk', device: 'Edge on Windows', createdAt: '2026-11-12T04:00:00Z', lastSeenAt: '2026-11-12T09:30:00Z', current: false },
      { id: 'here', device: 'Chrome on macOS', createdAt: '2026-11-13T03:30:00Z', lastSeenAt: '2026-11-13T05:00:00Z', current: true },
    ],
    ended: [] as { id: string; csrf: string | undefined }[],
  };
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: receptionist }));
  await page.route('**/v1/me/sessions', r => r.fulfill({ json: state.sessions }));
  await page.route('**/v1/me/sessions/*', r => {
    const id = r.request().url().split('/').at(-1)!;
    state.ended.push({ id, csrf: r.request().headers()['x-medvya-csrf'] });
    state.sessions = state.sessions.filter(s => s.id !== id);
    return r.fulfill({ status: 204 });
  });
  await page.goto('/account/devices');
  return state;
}

test('lists this device first without a Sign out, and ends another device', async ({ page }) => {
  const state = await open(page);
  const table = page.getByRole('region', { name: 'Devices where you are signed in' });
  const rows = table.getByRole('row');
  await expect(rows.nth(1)).toContainText('Chrome on macOS');
  await expect(rows.nth(1)).toContainText('This device');
  await expect(rows.nth(1).getByRole('button')).toHaveCount(0);
  await expect(rows.nth(2)).toContainText('Edge on Windows');
  await rows.nth(2).getByRole('button', { name: /Sign out Edge on Windows/ }).click();
  await expect(page.getByText('That device was signed out.')).toBeVisible();
  await expect(table.getByRole('row')).toHaveCount(2);
  expect(state.ended).toEqual([{ id: 'desk', csrf: '1' }]);
});

test('every staff role reaches it from the account menu', async ({ page }) => {
  await open(page);
  await page.setViewportSize({ width: 1366, height: 800 }); // the account menu shows from 768 px; phones use More
  await page.goto('/schedule');
  await page.getByRole('button', { name: /^Signed in as / }).click();
  await page.getByRole('menuitem', { name: 'My devices' }).click();
  await expect(page).toHaveURL(/\/account\/devices$/);
  await expect(page.getByRole('heading', { name: 'My devices' })).toBeVisible();
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const locale of ['en', 'ar'])
  test(`axe finds no violations on My devices (${locale}); 44 px targets`, async ({ page }) => {
    await open(page, locale);
    await expect(page.getByRole('heading', { name: locale === 'ar' ? 'أجهزتي' : 'My devices' })).toBeVisible();
    if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    for (const b of await page.getByRole('table').getByRole('button').all()) expect((await b.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
        .map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
  });
