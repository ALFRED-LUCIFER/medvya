import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P1-041. CI has no API: the metrics endpoints are mocked. The server rules (owner/admin only, tenant and facility isolation,
// the numbers themselves) are tested on PostgreSQL in PilotMetricsTests.cs and AuthorisationMatrixTests.cs.
const facility = '11111111-1111-4111-8111-111111111111';
const owner = {
  user: { id: 'u-rao', displayName: 'Dr. Arjun Rao (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: facility, facilityName: 'Adyar', role: 'SoloDoctor' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: facility, role: 'SoloDoctor', flags: [], capabilities: { StaffRolesIntegrations: 'Config', RegisterBookPatients: 'Full' } },
};
const metrics = {
  from: '2026-10-24T05:30:00Z', to: '2026-11-07T05:30:00Z',
  checkIn: { returningMedianSeconds: 74, returningCount: 18, medianSeconds: 81, count: 25, targetSeconds: 60 },
  noShows: { noShows: 6, attended: 54, percent: 10 },
  doubleBookingRefusals: 0,
  baselines: [{ metric: 'NoShowPercent', value: 18, method: 'Paper register, every visit', fromDate: '2026-10-05', toDate: '2026-10-18', recordedAt: '2026-10-19T05:30:00Z' }],
};
const delivery = { channels: [{ channel: 'Email', total: 40, delivered: 36, failed: 2, pending: 2 }], failures: [] };

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
async function axe(page: Page) {
  await page.addScriptTag({ path: axePath });
  return page.evaluate(async () =>
    (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
      .map(v => `${v.id} (${v.nodes.length})`));
}

async function setUp(page: Page, locale = 'en') {
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: owner }));
  await page.route('**/v1/notifications/delivery**', r => r.fulfill({ json: delivery }));
}

test('the owner sees the pilot numbers beside the baseline, with the check-in gap written out, and records a baseline', async ({ page }) => {
  await setUp(page);
  const asked: string[] = [];
  await page.route('**/v1/schedule/metrics**', r => {
    if (r.request().method() === 'POST') return r.fulfill({ json: { ...metrics.baselines[0], metric: 'CheckInSeconds', value: 140 } });
    asked.push(new URL(r.request().url()).search);
    return r.fulfill({ json: metrics });
  });
  await page.goto('/settings?tab=metrics');
  const report = page.getByRole('region', { name: 'Pilot metrics' });
  await expect(report).toContainText('74 s');
  await expect(report).toContainText('14 s over the 60 s target.'); // exit gate 3: the gap is reported, not only coloured
  await expect(report).toContainText('18 returning of 25 timed check-ins; all patients: 81 s.');
  await expect(report).toContainText('10%');
  await expect(report).toContainText('Before Medvya: 18% (');
  await expect(report).toContainText('94.7%'); // 36 of 38 messages with an outcome
  expect(asked[0]).toBe(`?facilityId=${facility}&days=14`);
  expect(await axe(page)).toEqual([]);

  await page.getByRole('button', { name: 'Record baseline' }).click();
  await expect(page.getByText('Say how it was measured (up to 200 characters).')).toBeVisible();
  await page.getByRole('combobox', { name: 'Metric' }).click();
  await page.getByRole('option', { name: 'Check-in time (seconds, median)' }).click();
  await page.getByLabel('Value').fill('140');
  await page.getByLabel('How it was measured').fill('Stopwatch, 20 patients');
  await page.getByLabel('Measured from').fill('2026-10-12');
  await page.getByLabel('Measured to').fill('2026-10-12');
  const posted = page.waitForRequest(r => r.url().endsWith('/v1/schedule/metrics/baselines') && r.method() === 'POST');
  await page.getByRole('button', { name: 'Record baseline' }).click();
  expect((await posted).postDataJSON()).toEqual({ facilityId: facility, metric: 'CheckInSeconds', value: 140, method: 'Stopwatch, 20 patients', fromDate: '2026-10-12', toDate: '2026-10-12' });
  await expect(page.getByText('Baseline recorded.')).toBeVisible();
});

test('the report reads right to left in Arabic and stays axe clean', async ({ page }) => {
  await setUp(page, 'ar');
  await page.route('**/v1/schedule/metrics**', r => r.fulfill({ json: metrics }));
  await page.goto('/settings?tab=metrics');
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await expect(page.getByRole('region', { name: 'مؤشرات التجربة' })).toBeVisible();
  await expect(page.getByText('نسبة عدم الحضور', { exact: true })).toBeVisible();
  expect(await axe(page)).toEqual([]);
});

test('a role the server refuses sees why, and no numbers', async ({ page }) => {
  await setUp(page);
  await page.route('**/v1/schedule/metrics**', r => r.fulfill({ status: 403, json: { title: 'No access at this facility' } }));
  await page.goto('/settings?tab=metrics');
  await expect(page.getByRole('alert')).toHaveText('Only the practice owner or admin can see the pilot metrics.');
});
