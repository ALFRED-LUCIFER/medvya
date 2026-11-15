import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';
import { t } from './support/desk';
import { commitVisible } from './support/oneScreen';

// MDV-P5-042, ADR-019: the console's Plans page against a mocked console API. The server rules (a save is a new version and never changes an old
// one, money in minor units, owner and finance only, a reason on every save, the audit entry) are tested on PostgreSQL in PlatformPlansTests.cs;
// here the browser path is real: the list per country, the edit form's minor units and reason, history, read-only roles, Arabic and axe.
test.use({ baseURL: 'http://localhost:4175' });

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
async function axe(page: Page) {
  await page.addScriptTag({ path: axePath });
  return page.evaluate(async () =>
    (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: { html: string }[] }[] }> } }).axe.run()).violations
      .map(v => `${v.id} (${v.nodes.length}) ${v.nodes.map(n => n.html.slice(0, 160)).join(' | ')}`));
}

const approved = { createdAt: '2026-11-06T00:00:00Z', createdBy: null, reason: 'Approved price list 2026.11 (ADR-019)' };
const prices = (inr: [number | null, number | null, number | null], aed: [number | null, number | null, number | null]) => [
  { country: 'IN', currency: 'INR', monthly: inr[0], yearly: inr[1], assistMonthly: inr[2] },
  { country: 'AE', currency: 'AED', monthly: aed[0], yearly: aed[1], assistMonthly: aed[2] },
];
const versions = [
  { id: 'c1', code: 'clinic', version: 1, perDoctor: true, custom: false, doctors: null, staff: 15, locations: null, assistVisitsPerDoctorMonth: 300, trialDays: 14,
    features: ['ai.ambient_notes', 'patient_app.booking'], prices: prices([349900, 3499000, null], [59900, 599000, null]), ...approved },
  { id: 'h1', code: 'hospital', version: 1, perDoctor: false, custom: true, doctors: null, staff: null, locations: null, assistVisitsPerDoctorMonth: null, trialDays: 0,
    features: ['ai.ambient_notes', 'auth.sso', 'integrations.his', 'patient_app.booking'], prices: prices([null, 25000000, null], [null, 4000000, null]), ...approved },
  { id: 's1', code: 'solo', version: 1, perDoctor: false, custom: false, doctors: 1, staff: 2, locations: 1, assistVisitsPerDoctorMonth: 300, trialDays: 14,
    features: ['patient_app.booking'], prices: prices([199900, 1999000, 99900], [34900, 349000, 14900]), ...approved },
];
const me = (role: string) => ({ displayName: 'Finance Lead (synthetic)', role, keyVerified: true, hasKey: true, canRegister: false });

async function mock(page: Page, role: string) {
  const posts: { path: string; body: Record<string, unknown> }[] = [];
  const list: object[] = [...versions];
  await page.route('**/v1/me', r => r.fulfill({ json: me(role) }));
  await page.route('**/v1/plans', r => r.fulfill({ json: list }));
  await page.route('**/v1/plans/*/versions', r => {
    const body = r.request().postDataJSON() as Record<string, unknown>;
    posts.push({ path: new URL(r.request().url()).pathname, body });
    const base = versions.find(v => v.code === 'solo')!;
    const saved = { ...base, ...body, id: 's2', version: 2, createdAt: '2026-11-15T05:00:00Z', createdBy: 'Finance Lead (synthetic)',
      prices: (body.prices as { country: string }[]).map(p => ({ ...p, currency: p.country === 'IN' ? 'INR' : 'AED' })) };
    list.unshift(saved);
    return r.fulfill({ status: 201, json: saved });
  });
  return posts;
}

test('finance saves a new Solo version: the form sends minor units, the whole plan and a reason, and history shows both versions', async ({ page }) => {
  const posts = await mock(page, 'Finance');
  await page.goto('/plans');
  await expect(page.getByRole('heading', { name: 'Plans and entitlements' })).toBeVisible();
  const table = page.getByRole('region', { name: 'Plans and entitlements' });
  const solo = table.getByRole('row').filter({ hasText: 'Solo' });
  await expect(solo).toContainText('₹1,999.00 per month');
  await expect(solo).toContainText('AED 349.00 per month');
  await expect(solo).toContainText('Assist add-on: ₹999.00 per doctor a month');
  await expect(solo).toContainText('Doctors: 1');
  await expect(table.getByRole('row').filter({ hasText: 'Clinic' })).toContainText('₹3,499.00 per doctor a month');
  await expect(table.getByRole('row').filter({ hasText: 'Hospital' })).toContainText('from ₹2,50,000.00 a year');
  expect(await axe(page)).toEqual([]);

  await page.getByRole('button', { name: 'Edit Solo' }).click();
  await expect(page.getByRole('heading', { name: 'Solo: new version 2' })).toBeVisible();
  const india = page.getByRole('group', { name: 'India (INR)' });
  await india.getByLabel('Per month').fill('2199.50');
  const save = page.locator('[data-slot=sticky-action-bar]').getByRole('button', { name: 'Save as new version' });
  await commitVisible(page, save);
  await save.click();
  await expect(page.getByRole('link', { name: 'Reason: Give a reason of at least 5 characters.' })).toBeVisible(); // the error summary
  expect(posts).toEqual([]); // nothing is sent without a reason
  await page.getByRole('checkbox', { name: 'Patient app trackers' }).check();
  await page.getByLabel('Reason').fill('Price list 2027.01 approved by the owner');
  expect(await axe(page)).toEqual([]);
  await save.click();
  await expect(page.getByRole('status')).toContainText('Solo version 2 saved.');
  expect(posts).toEqual([{ path: '/v1/plans/solo/versions', body: {
    baseVersion: 1, doctors: 1, staff: 2, locations: 1, assistVisitsPerDoctorMonth: 300, trialDays: 14,
    features: ['patient_app.booking', 'patient_app.trackers'],
    prices: [{ country: 'IN', monthly: 219950, yearly: 1999000, assistMonthly: 99900 }, { country: 'AE', monthly: 34900, yearly: 349000, assistMonthly: 14900 }],
    reason: 'Price list 2027.01 approved by the owner',
  } }]);

  await page.getByRole('button', { name: 'History of Solo' }).click();
  const history = page.getByRole('region', { name: 'Solo: every version' });
  await expect(history.getByRole('rowheader')).toHaveText(['Version 2', 'Version 1']);
  await expect(history.getByRole('row').nth(2)).toContainText('Approved price list');
});

test('when someone saved a newer version first, the save is refused and the editor reloads it without losing what they typed', async ({ page }) => {
  await page.route('**/v1/me', r => r.fulfill({ json: me('Owner') }));
  let list: object[] = versions;
  await page.route('**/v1/plans', r => r.fulfill({ json: list }));
  const bases: unknown[] = [];
  await page.route('**/v1/plans/*/versions', r => {
    const body = r.request().postDataJSON() as { baseVersion: number };
    bases.push(body.baseVersion);
    return body.baseVersion === 1 ? r.fulfill({ status: 409, json: { title: 'The plan has a newer version' } }) : r.fulfill({ status: 201, json: { ...versions[2], ...body, version: 3 } });
  });
  await page.goto('/plans');
  await page.getByRole('button', { name: 'Edit Solo' }).click();
  await page.getByRole('group', { name: 'India (INR)' }).getByLabel('Per month').fill('2299');
  await page.getByLabel('Reason').fill('Price list 2027.01 approved by the owner');
  list = [{ ...versions[2], id: 's2', version: 2 }, ...versions]; // another owner's save
  const bar = page.locator('[data-slot=sticky-action-bar]');
  await bar.getByRole('button', { name: 'Save as new version' }).click();
  await expect(bar.getByRole('alert')).toContainText('Someone saved a newer version');
  await bar.getByRole('button', { name: 'Reload latest version' }).click();
  await expect(page.getByRole('heading', { name: 'Solo: new version 3' })).toBeVisible();
  await expect(page.getByRole('group', { name: 'India (INR)' }).getByLabel('Per month')).toHaveValue('2299');
  await bar.getByRole('button', { name: 'Save as new version' }).click();
  await expect(page.getByRole('status')).toContainText('Solo version 3 saved.');
  expect(bases).toEqual([1, 2]);
});

for (const role of ['Operations', 'Support', 'Reviewer']) {
  test(`${role} reads plans but has no edit`, async ({ page }) => {
    await mock(page, role);
    await page.goto('/plans');
    await expect(page.getByRole('region', { name: 'Plans and entitlements' }).getByRole('row')).toHaveCount(4);
    await expect(page.getByRole('button', { name: /^Edit / })).toHaveCount(0);
  });
}

test('the plans page and its edit form read in Arabic, right to left, axe clean, and stack at 375 px', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('medvya.locale', 'ar'));
  await mock(page, 'Owner');
  await page.goto('/plans');
  expect(await page.evaluate(() => document.documentElement.dir)).toBe('rtl');
  await expect(page.getByRole('heading', { name: t('ar', 'platform.plans.title') })).toBeVisible();
  expect(await axe(page)).toEqual([]);
  await page.getByRole('button', { name: t('ar', 'platform.plans.editFor', { plan: 'Solo' }) }).click();
  await expect(page.getByRole('group', { name: t('ar', 'platform.plans.country.IN') })).toBeVisible();
  expect(await axe(page)).toEqual([]);

  await page.setViewportSize({ width: 375, height: 740 });
  await page.locator('[data-slot=sticky-action-bar]').getByRole('button', { name: t('ar', 'platform.action.cancel') }).click();
  const region = page.getByRole('region', { name: t('ar', 'platform.plans.title') });
  await expect(region.getByRole('rowheader').first()).toContainText('Clinic');
  await expect(region.getByRole('row').nth(1).getByRole('cell').getByText(t('ar', 'platform.plans.limits'), { exact: true })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(0);
  expect(await axe(page)).toEqual([]);
});
