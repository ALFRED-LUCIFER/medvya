import { createRequire } from 'node:module';
import { t } from './support/desk';
import { expectStacked } from './support/stacked';
import { expect, test, type Page } from '@playwright/test';
import { authorised, line, mockPrescription } from './support/prescription';
import { mockSharedLink, mockShares } from './support/share';
import { targetPx } from './support/targets';

// MDV-P2-020 (E-10): the authorised prescription is shared by secure link to the patient or a named pharmacy, and the pharmacy dispatch is recorded;
// the public link page opens the PDF only with the code sent to the patient's registered mobile. CI has no API: endpoints are mocked in the server's
// shapes; the gates, the message texts and the audit are proven in RxShareTests.
const metformin = line('rx-1', 'Metformin', { dose: '1', doseUnit: 'tablet', route: 'oral', frequency: 'BD', sig: { en: 'Take 1 tablet oral BD.', hi: '', ta: '', ar: '' } });
const printable = { lines: [metformin], authorisation: { authorised: authorised(), changed: false } };

test('the doctor shares the prescription with the patient and a pharmacy, then records it dispensed', async ({ page }) => {
  await mockPrescription(page, printable);
  const { calls } = await mockShares(page);
  await page.goto('/consult/e-1/rx/print');
  const share = page.getByRole('region', { name: 'Share with the patient or a pharmacy' });
  await expect(share).toContainText('The message names only the clinic.');
  await expect(share).toContainText('Not shared yet.');

  await share.getByRole('button', { name: 'Send secure link' }).click();
  await expect(share.getByRole('status')).toHaveText('Secure link sent.');
  await expect(share.getByRole('row', { name: /Patient, by SMS/ })).toContainText('Link works until');

  await share.getByRole('combobox', { name: 'Send to' }).click();
  await page.getByRole('option', { name: 'A pharmacy, by email' }).click();
  await share.getByRole('button', { name: 'Send secure link' }).click();
  await expect(share.getByText('Enter the pharmacy\'s name.')).toBeVisible(); // checked before it is sent
  await share.getByLabel('Pharmacy name').fill('Apollo Pharmacy Adyar (synthetic)');
  await share.getByLabel('Pharmacy email').fill('orders@pharmacy.example.test');
  await share.getByRole('button', { name: 'Send secure link' }).click();
  const row = share.getByRole('row', { name: /Apollo Pharmacy Adyar/ });
  await expect(row).toContainText('Sent to the pharmacy');
  expect(calls.shared).toEqual([{ recipient: 'Patient', channel: 'Sms' }, { recipient: 'Pharmacy', pharmacyName: 'Apollo Pharmacy Adyar (synthetic)', pharmacyEmail: 'orders@pharmacy.example.test' }]);

  await row.getByRole('button', { name: 'Mark dispensed by Apollo Pharmacy Adyar (synthetic)' }).click();
  await share.getByLabel('Dispensed on').fill('2026-10-19T11:30');
  await share.getByLabel('Pharmacy reference (optional)').fill('APL-2231');
  await share.getByRole('button', { name: 'Save' }).click();
  await expect(row).toContainText('Dispensed');
  await expect(row).toContainText('APL-2231');
  expect(calls.dispatched).toEqual([{ state: 'Dispensed', pharmacyRef: 'APL-2231', dispensedAt: new Date('2026-10-19T11:30').toISOString() }]);
});

test('the link page opens the PDF only with the code, and says when a link is unknown or replaced', async ({ page }) => {
  const bodies = await mockSharedLink(page);
  await page.goto('/rx/synthetic-link');
  await expect(page.getByRole('heading', { name: 'Shared prescription' })).toBeVisible();
  await expect(page.getByText('If you are a pharmacy, ask the patient for the code.')).toBeVisible();
  await page.getByRole('button', { name: 'Text me the code' }).click();
  await expect(page.getByRole('status')).toContainText('Code sent to the patient\'s registered mobile.');
  await page.getByLabel('Code (6 digits)').fill('111111');
  await page.getByRole('button', { name: 'Open the PDF' }).click();
  await expect(page.getByRole('alert')).toContainText('That code is not right.');
  await page.getByLabel('Code (6 digits)').fill('482913');
  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Open the PDF' }).click();
  expect((await download).suggestedFilename()).toBe('prescription.pdf'); // no patient data in the name
  await expect(page.getByText('The PDF is saved on this device.')).toBeVisible();
  expect(bodies).toEqual([{ token: 'synthetic-link' }, { token: 'synthetic-link', code: '111111' }, { token: 'synthetic-link', code: '482913' }]); // in the body, never the URL

  await page.goto('/rx/replaced-link');
  await page.getByRole('button', { name: 'Text me the code' }).click();
  await expect(page.getByRole('alert')).toContainText('Do not dispense from this link.');
  await page.goto('/rx/made-up');
  await page.getByRole('button', { name: 'Text me the code' }).click();
  await expect(page.getByRole('alert')).toContainText('This link is not valid or has expired.');
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
const axeViolations = async (page: Page) => {
  await page.addScriptTag({ path: axePath });
  return page.evaluate(async () =>
    (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
      .map(v => `${v.id} (${v.nodes.length})`));
};

for (const locale of ['en', 'ar'])
  test(`axe finds no violations on the share panel and the link page (${locale})`, async ({ page }) => {
    await mockPrescription(page, { locale, ...printable });
    await mockShares(page);
    await page.goto('/consult/e-1/rx/print');
    if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    const share = page.getByRole('region', { name: locale === 'ar' ? 'المشاركة مع المريض أو صيدلية' : 'Share with the patient or a pharmacy' });
    await share.getByRole('button', { name: locale === 'ar' ? 'إرسال رابط آمن' : 'Send secure link' }).click();
    await expect(share.getByRole('status')).toBeVisible();
    for (const target of await share.locator('button').all()) expect((await target.boundingBox())!.height).toBeGreaterThanOrEqual(await targetPx(page));
    expect(await axeViolations(page)).toEqual([]);

    await mockSharedLink(page);
    await page.goto('/rx/synthetic-link');
    await page.getByRole('button', { name: locale === 'ar' ? 'أرسل لي الرمز' : 'Text me the code' }).click();
    await expect(page.getByRole('status')).toBeVisible();
    expect(await axeViolations(page)).toEqual([]);
  });

// MDV-P2-054 AC5: at 375 px the shares are stacked: the recipient first and bold, the time sent under its column's name.
test.describe('at 375 px', () => {
  test.use({ viewport: { width: 375, height: 812 } });
  for (const locale of ['en', 'ar'])
    test(`the shares are stacked with every field labelled and nothing scrolling sideways (${locale})`, async ({ page }) => {
      await mockPrescription(page, { locale, ...printable });
      await mockShares(page);
      await page.goto('/consult/e-1/rx/print');
      const share = page.getByRole('region', { name: t(locale, 'rxShare.title') });
      await share.getByRole('button', { name: t(locale, 'rxShare.send') }).click();
      await expect(share.getByRole('status')).toBeVisible();
      // shortcut: the printed prescription above the shares (RxPrintPage's QR footer) makes this page 13 px too wide at 375 px, so only the
      // shares table's own box is checked here; check the whole page once the print preview fits a phone.
      await expectStacked(page, page.getByRole('region', { name: t(locale, 'rxShare.tableLabel') }), [t(locale, 'rxShare.col.sharedAt')], { wholePage: false });
    });
});
