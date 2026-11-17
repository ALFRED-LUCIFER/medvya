import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';
import { mockBilling } from './support/billing';

// MDV-P4-002 (F-11): Settings, Services and Settings, Billing. CI has no API, so the endpoints are mocked (support/billing.ts).

// #1430: while billing is off, the Billing group holds only the owner's switch; switching on (after a confirm) refetches the session, so Services,
// the billing settings and the Billing menu appear at once. Who may switch, the audit and the release gate: BillingCatalogueTests on PostgreSQL.
test('the owner switches billing on in Settings, and Services and the Billing menu appear', async ({ page }) => {
  const api = await mockBilling(page, { flags: [] });
  await page.goto('/settings?tab=services');
  await expect(page.getByRole('tab', { name: 'Profile' })).toHaveAttribute('aria-selected', 'true'); // Services is not there yet: falls back to Profile
  await page.getByRole('tab', { name: 'Billing' }).click();
  await expect(page.getByRole('tab', { name: 'Services' })).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Billing' })).toHaveCount(0);
  const toggle = page.getByRole('region', { name: 'Billing' }).getByRole('switch', { name: /^Billing and invoices/ });
  await expect(toggle).toHaveAttribute('aria-checked', 'false');
  await toggle.click();
  const dialog = page.getByRole('dialog', { name: 'Switch billing on?' });
  await dialog.getByRole('button', { name: 'Keep it as it is' }).click();
  await expect(toggle).toHaveAttribute('aria-checked', 'false');
  expect(api.posts).toEqual([]); // nothing changes until the confirm
  await toggle.click();
  await dialog.getByRole('button', { name: 'Switch on' }).click();
  await expect(toggle).toHaveAttribute('aria-checked', 'true');
  expect(api.posts).toEqual([{ path: 'billing-switch', body: { enabled: true } }]);
  await expect(page.getByRole('tab', { name: 'Services' })).toBeVisible();
  await expect(page.getByLabel('Invoice prefix')).toBeVisible();
  await expect(page.getByRole('link', { name: 'Billing' })).toBeVisible();
});

test('the billing switch says it is not available while Medvya has not released billing to the practice', async ({ page }) => {
  await mockBilling(page, { flags: [], released: false });
  await page.goto('/settings?tab=billing');
  await expect(page.getByRole('region', { name: 'Billing' })).toContainText('Billing is not available for your practice yet.');
  await expect(page.getByRole('switch')).toHaveCount(0);
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
  // Razorpay links need the clinic's own account first (MDV-P4-007).
  await expect(page.getByText('Connect Razorpay below before offering Razorpay payment links.')).toBeVisible();
  await connectRazorpay(page, 'en');
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
      } else {
        await expect(page.getByRole('button', { name: locale === 'ar' ? 'حفظ' : 'Save' })).toBeVisible();
        await expect(page.getByRole('switch', { name: locale === 'ar' ? /^الفوترة والفواتير/ : /^Billing and invoices/ })).toHaveAttribute('aria-checked', 'true');
      }
      if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
      await page.addScriptTag({ path: axePath });
      const violations = await page.evaluate(async () =>
        (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations.map(v => `${v.id} (${v.nodes.length})`));
      expect(violations).toEqual([]);
    });

for (const locale of ['en', 'ar'] as const)
  test(`axe finds no violations on the billing switch and its confirm while billing is off (${locale})`, async ({ page }) => {
    await mockBilling(page, { locale, flags: [] });
    await page.goto('/settings?tab=billing');
    if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await page.getByRole('switch').click();
    await expect(page.getByRole('dialog', { name: locale === 'ar' ? 'تشغيل الفوترة؟' : 'Switch billing on?' })).toBeVisible();
    expect(await axeViolations(page)).toEqual([]);
  });

// MDV-P4-007: Settings, Billing, Razorpay. Synthetic test keys only; the mocked API answers in RazorpayStatus's shape.
const razorpayWords = {
  en: { connect: 'Connect Razorpay', keyId: 'Key id', keySecret: 'Key secret', webhookSecret: 'Webhook secret', save: 'Connect', connected: 'Connected · Test · key …0001', format: 'A key id starts with rzp_test_ or rzp_live_, then letters and digits.' },
  ar: { connect: 'ربط Razorpay', keyId: 'معرّف المفتاح', keySecret: 'سر المفتاح', webhookSecret: 'سر Webhook', save: 'ربط', connected: 'مرتبط · اختبار · المفتاح …0001', format: 'يبدأ معرّف المفتاح بـ rzp_test_ أو rzp_live_ ثم أحرف وأرقام.' },
};

async function axeViolations(page: Page) {
  await page.addScriptTag({ path: axePath });
  return page.evaluate(async () =>
    (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations.map(v => `${v.id} (${v.nodes.length})`));
}

async function connectRazorpay(page: Page, locale: 'en' | 'ar', checkAxe = false) {
  const w = razorpayWords[locale];
  await page.getByRole('button', { name: w.connect }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel(w.keyId, { exact: true }).fill('key_0001');
  await dialog.getByRole('button', { name: w.save, exact: true }).click();
  await expect(dialog.getByText(w.format)).toBeVisible();
  if (checkAxe) expect(await axeViolations(page)).toEqual([]);
  await dialog.getByLabel(w.keyId, { exact: true }).fill('rzp_test_Synth0001');
  await dialog.getByLabel(w.keySecret, { exact: true }).fill('synthetic-key-secret');
  await dialog.getByLabel(w.webhookSecret, { exact: true }).fill('synthetic-webhook-secret');
  await dialog.getByRole('button', { name: w.save, exact: true }).click();
  await expect(dialog).toBeHidden();
  await expect(page.getByText(w.connected)).toBeVisible();
}

for (const locale of ['en', 'ar'] as const)
  test(`the admin connects Razorpay and gets the webhook URL to paste, never a secret back (${locale})`, async ({ page }) => {
    const api = await mockBilling(page, { locale, country: 'IN' });
    await page.goto('/settings?tab=billing');
    if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await connectRazorpay(page, locale, true);
    expect(api.posts.at(-1)).toEqual({ path: 'razorpay', body: { keyId: 'rzp_test_Synth0001', keySecret: 'synthetic-key-secret', webhookSecret: 'synthetic-webhook-secret' } });
    await expect(page.getByText(`/v1/webhooks/razorpay/${'a'.repeat(64)}`)).toBeVisible();
    await expect(page.getByText('payment_link.paid', { exact: true })).toBeVisible();
    expect(await page.content()).not.toContain('synthetic-key-secret');
    expect(await axeViolations(page)).toEqual([]);
  });
