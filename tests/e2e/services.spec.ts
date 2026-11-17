import { createRequire } from 'node:module';
import { expect, test } from '@playwright/test';
import { mockBilling } from './support/billing';

// MDV-P4-002 (F-11): Settings, Services and Settings, Billing. CI has no API, so the endpoints are mocked (support/billing.ts).

test('Services and Billing exist in Settings only while billing is on', async ({ page }) => {
  await mockBilling(page, { flags: [] });
  await page.goto('/settings?tab=services');
  await expect(page.getByRole('tab', { name: 'Billing' })).toHaveCount(0);
  await expect(page.getByRole('tab', { name: 'Profile' })).toHaveAttribute('aria-selected', 'true'); // an unknown tab falls back to Profile
});

test('the admin adds a service, then a doctor’s own price, and sees the invoice line it makes', async ({ page }) => {
  const api = await mockBilling(page);
  await page.goto('/settings?tab=services');
  await expect(page.getByRole('row', { name: /Consultation \(new visit\)/ })).toContainText('₹500.00');

  await page.getByRole('button', { name: 'Add service' }).click();
  const drawer = page.getByRole('dialog', { name: 'New service' });
  await drawer.getByLabel('Name').fill('ECG (synthetic)');
  await drawer.getByLabel('SAC').fill('12345');
  await drawer.getByRole('button', { name: 'Save service' }).click();
  await expect(drawer.getByText('Check this value.')).toBeVisible(); // five digits: the form's own check
  await drawer.getByLabel('SAC').fill('999999');
  await drawer.getByRole('button', { name: 'Save service' }).click();
  await expect(drawer.getByText('This is not in the right format.')).toBeVisible(); // the server's refusal, under the field
  await drawer.getByLabel('SAC').fill('999316');
  await drawer.getByRole('button', { name: 'Save service' }).click();
  await expect(page.getByRole('dialog', { name: 'ECG (synthetic)' })).toBeVisible();
  expect(api.posts.at(-1)).toMatchObject({ path: 'services', body: { name: 'ECG (synthetic)', sac: '999316', taxTreatment: 'Exempt', gstRateBasisPoints: null } });

  const editor = page.getByRole('dialog', { name: 'ECG (synthetic)' });
  await expect(editor.getByText('Tax treatment must be confirmed with your tax advisor.')).toBeVisible();
  await expect(editor.getByText('Saving creates a new price version effective from the date above. Issued invoices keep the price they were issued with.')).toBeVisible();
  await editor.getByLabel('Price (INR)').fill('450');
  await editor.getByRole('button', { name: 'Save version' }).click();
  await expect(editor.getByText('Line total ₹450.00')).toBeVisible();
  await editor.getByRole('combobox', { name: 'Price for' }).click();
  await page.getByRole('option', { name: 'Dr. Arjun Rao (synthetic), Adyar' }).click();
  await editor.getByLabel('Price (INR)').fill('600');
  await editor.getByRole('button', { name: 'Save version' }).click();
  await expect(editor.getByRole('row', { name: /Dr. Arjun Rao/ })).toContainText('₹600.00');
  expect(api.posts.at(-1)).toMatchObject({ body: { doctorId: 'dr-1', amountMinor: 60_000 } });
});

test('billing settings are saved, and a prefix the country refuses is shown under its field', async ({ page }) => {
  const api = await mockBilling(page);
  await page.goto('/settings?tab=billing');
  await page.getByLabel('Invoice prefix').fill('INVO');
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByText('This is not in the right format.')).toBeVisible();
  await page.getByLabel('Invoice prefix').fill('INB');
  await page.getByRole('checkbox', { name: 'Razorpay payment link' }).check();
  await page.getByLabel('Discount reasons').fill('Staff family\nSenior citizen');
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Saved' })).toBeVisible();
  expect(api.posts.at(-1)?.body).toMatchObject({ invoicePrefix: 'INB', paymentMethods: ['Cash', 'Card', 'UpiDirect', 'RazorpayLink'], discountReasons: ['Staff family', 'Senior citizen'] });
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const locale of ['en', 'ar'])
  for (const tab of ['services', 'billing'])
    test(`axe finds no violations on Settings, ${tab} (${locale})`, async ({ page }) => {
      await mockBilling(page, { locale, country: locale === 'ar' ? 'AE' : 'IN' });
      await page.goto(`/settings?tab=${tab}`);
      if (tab === 'services') {
        await page.getByRole('row', { name: /Consultation/ }).getByRole('button').click();
        await expect(page.getByRole('dialog')).toBeVisible();
        await expect(page.getByRole('dialog').getByRole('table')).toBeVisible();
      } else await expect(page.getByRole('button', { name: locale === 'ar' ? 'حفظ' : 'Save' })).toBeVisible();
      if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
      await page.addScriptTag({ path: axePath });
      const violations = await page.evaluate(async () =>
        (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations.map(v => `${v.id} (${v.nodes.length})`));
      expect(violations).toEqual([]);
    });
