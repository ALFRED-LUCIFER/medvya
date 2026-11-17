import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';
import { mockInvoices } from './support/billing';

// MDV-P4-004 (P-06): the bills list and one bill with split payments. CI has no API, so the endpoints are mocked (support/billing.ts mockInvoices).

async function issued(page: Page) {
  await page.goto('/billing/inv-1');
  await page.getByRole('row', { name: /Consultation \(follow-up\)/ }).getByRole('button', { name: 'Confirm' }).click();
  await page.getByRole('button', { name: 'Issue bill' }).click();
  await expect(page.getByRole('heading', { name: 'Bill INV/26-27/00001' })).toBeVisible();
}

test('the list shows each bill with its state in words and an icon, and opens it', async ({ page }) => {
  await mockInvoices(page);
  await page.goto('/billing');
  const row = page.getByRole('row', { name: /Meera Iyer \(synthetic\)/ });
  await expect(row).toContainText('Draft');
  await expect(row.locator('[data-slot="chip"] svg')).toHaveCount(1);
  await row.getByRole('button', { name: 'Draft (no number yet)' }).click();
  await expect(page).toHaveURL(/\/billing\/inv-1$/);
  await expect(page.getByRole('heading', { name: 'Draft bill' })).toBeVisible();
});

test('a bill is paid in parts by cash and card; a card number and an over-collection are refused', async ({ page }) => {
  const api = await mockInvoices(page);
  await issued(page);
  await page.getByLabel('Amount (INR)').fill('100');
  await expect(page.getByText('Balance remaining ₹200.00')).toBeVisible();
  await page.getByRole('button', { name: 'Record payment' }).click();
  await expect(page.getByText('Partly paid')).toBeVisible();

  await page.getByRole('combobox', { name: 'Method' }).click();
  await page.getByRole('option', { name: "Card (on the clinic's terminal)" }).click();
  await page.getByLabel('Amount (INR)').fill('250');
  await expect(page.getByText('Over-collected by ₹50.00 · reduce an amount')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Record payment' })).toBeDisabled();
  await page.getByRole('button', { name: 'Use the balance' }).click();
  await expect(page.getByText('Fully covered')).toBeVisible();
  await page.getByLabel('Terminal reference').fill('4111 1111 1111 1111');
  await page.getByRole('button', { name: 'Record payment' }).click();
  await expect(page.getByText('That looks like a card number.', { exact: false })).toBeVisible(); // refused on the device; never sent
  await page.getByLabel('Terminal reference').fill('TID 0042 APPR 7781');
  await page.getByRole('button', { name: 'Record payment' }).click();
  await expect(page.getByText('Paid', { exact: true }).first()).toBeVisible();
  expect(api.posts.filter(p => p.path === 'payments').map(p => p.body)).toEqual([
    { method: 'Cash', amountMinor: 10_000, reference: null },
    { method: 'Card', amountMinor: 20_000, reference: 'TID 0042 APPR 7781' },
  ]);
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const locale of ['en', 'ar'])
  for (const view of ['list', 'draft', 'pending'] as const)
    test(`axe finds no violations on billing, ${view} (${locale})`, async ({ page }) => {
      await mockInvoices(page, { locale });
      if (view === 'list') await page.goto('/billing');
      else if (view === 'draft') await page.goto('/billing/inv-1');
      else {
        await page.goto('/billing/inv-1');
        await page.getByRole('row').nth(1).getByRole('button').first().click(); // Confirm
        await page.locator('footer').getByRole('button').last().click(); // Issue bill
        await page.locator('footer').getByRole('button').last().click(); // Razorpay link for the balance
        await expect(page.getByText('https://rzp.io/i/synthetic')).toBeVisible();
      }
      await expect(page.getByRole('table').first()).toBeVisible();
      if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
      await page.addScriptTag({ path: axePath });
      const violations = await page.evaluate(async () =>
        (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations.map(v => `${v.id} (${v.nodes.length})`));
      expect(violations).toEqual([]);
    });
