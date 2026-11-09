import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';
import { mockOrders } from './support/orders';

// MDV-P2-018 (E-05): the doctor composes investigation orders as drafts, switches one off, places the rest on purpose, and prints the slip.
// CI has no API: endpoints are mocked in the shapes the server sends. Drafts-until-placed, one audit row per call, no ordering by anything
// else, the timeline and the access checks are proven on PostgreSQL in OrderTests and OrderEndpointTests.
const orderRow = (page: Page, name: string) => page.getByRole('row', { name: new RegExp(name) });

test('orders stay drafts until Place orders, a switched-off test is not placed, and the slip shows the placed ones', async ({ page }) => {
  const { calls } = await mockOrders(page);
  await page.goto('/consult/e-1');
  await expect(page.getByText('No tests added yet.')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Place orders' })).toBeDisabled();

  await page.getByLabel('Test', { exact: true }).fill('HbA1c');
  await page.getByLabel('Instructions (optional), for example fasting 10 h').fill('fasting 10 h');
  await page.getByRole('button', { name: 'Add test' }).click();
  await page.getByLabel('Test', { exact: true }).fill('Lipid profile');
  await page.getByRole('button', { name: 'Add test' }).click();
  expect(calls.added[0]).toEqual({ kind: 'Lab', name: 'HbA1c', note: 'fasting 10 h' });
  await expect(orderRow(page, 'HbA1c')).toContainText('Draft'); // a word, with an icon
  await expect(orderRow(page, 'HbA1c')).toContainText('fasting 10 h');

  await page.getByRole('switch', { name: 'Include Lipid profile' }).click();
  await expect(orderRow(page, 'Lipid profile')).toContainText('Not included');
  await page.getByLabel('Lab or centre (optional)').fill('Metro Diagnostics');
  await page.getByRole('button', { name: 'Place orders' }).click();
  await expect(page.getByText('Orders placed.')).toBeVisible();
  expect(calls.placed).toEqual([{ destination: 'Metro Diagnostics' }]);
  await expect(orderRow(page, 'HbA1c')).toContainText('Ordered');
  await expect(orderRow(page, 'Lipid profile')).toContainText('Not included');
  await expect(page.getByRole('switch', { name: 'Include HbA1c' })).toHaveCount(0); // an ordered test cannot be switched
  await expect(page.getByRole('button', { name: 'Place orders' })).toBeDisabled();

  await page.getByRole('link', { name: 'Investigation slip' }).click();
  await expect(page).toHaveURL(/\/consult\/e-1\/slip$/);
  const slip = page.getByRole('article');
  await expect(slip).toContainText('Meera Iyer (synthetic)');
  await expect(slip).toContainText('000001');
  await expect(slip).toContainText('Metro Diagnostics');
  await expect(slip).toContainText('fasting 10 h');
  await expect(slip).not.toContainText('Lipid profile');
  await expect(page.getByRole('button', { name: 'Print or save as PDF' })).toBeVisible();
});

test('an order set adds its tests as drafts', async ({ page }) => {
  const { calls } = await mockOrders(page);
  await page.goto('/consult/e-1');
  await page.getByRole('combobox', { name: 'Order set' }).click();
  await page.getByRole('option', { name: 'Diabetes review labs' }).click();
  await page.getByRole('button', { name: 'Add its tests' }).click();
  expect(calls.sets).toEqual([{ orderSetId: 's-1' }]);
  await expect(orderRow(page, 'Fasting glucose')).toContainText('Draft');
});

test('an ended consultation shows its orders and cannot change them', async ({ page }) => {
  await mockOrders(page, { ended: true });
  await page.goto('/consult/e-1');
  await expect(page.getByText('This consultation has ended. Orders can be read and the slip printed, not changed.')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Place orders' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Add test' })).toHaveCount(0);
});

test('a role that may not order sees no orders panel', async ({ page }) => {
  await mockOrders(page, { allowed: false });
  await page.goto('/consult/e-1');
  await expect(page.getByRole('region', { name: 'Patient identity' })).toBeVisible();
  await expect(page.getByRole('region', { name: 'Orders' })).toHaveCount(0);
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const locale of ['en', 'ar'])
  test(`axe finds no violations on the orders panel and the slip (${locale})`, async ({ page }) => {
    const { orders } = await mockOrders(page, { locale });
    orders.push({ id: 'o-1', kind: 'Lab', name: 'HbA1c', note: 'fasting 10 h', state: 'Ordered', destination: 'Metro Diagnostics', orderedAt: '2026-10-19T04:50:00Z' },
      { id: 'o-2', kind: 'Imaging', name: 'Chest X-ray', note: null, state: 'Draft', destination: null, orderedAt: null });
    for (const path of ['/consult/e-1', '/consult/e-1/slip']) {
      await page.goto(path);
      await expect(path.endsWith('slip') ? page.getByRole('article') : page.getByRole('region', { name: locale === 'ar' ? 'الطلبات' : 'Orders' })).toBeVisible();
      if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
      await page.addScriptTag({ path: axePath });
      const violations = await page.evaluate(async () =>
        (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations.map(v => `${v.id} (${v.nodes.length})`));
      expect(violations, path).toEqual([]);
    }
  });
