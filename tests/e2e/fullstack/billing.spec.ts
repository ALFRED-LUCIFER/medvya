import { createHash } from 'node:crypto';
import { expect, test, type Browser, type Page } from '@playwright/test';
import { expectArabicAxeClean, expectNoAxeViolations, signIn, syntheticPatient, walkIn, watchUrls } from './support';

// MDV-P4-004, MDV-P4-005, MDV-P4-012 through the real stack (real Host.Api, real PostgreSQL, real Blob): the owner switches billing on, the desk
// opens the drawer, a walk-in patient is checked in and billed, pays part cash and part card, the bill and receipt print as PDFs (a re-print is the
// same bytes), and the day closes short, so a second staff member confirms before the shift report prints. Razorpay is not called (no keys here).
const joseph = /A\. Joseph/;
const lakshmi = /K\. Lakshmi/;

async function setBilling(browser: Browser, enabled: boolean) {
  const admin = await browser.newPage();
  try {
    await signIn(admin, lakshmi);
    await admin.goto('/app/settings?tab=billing');
    const toggle = admin.getByRole('switch', { name: /^Billing/ });
    await expect(toggle).toBeVisible();
    if ((await toggle.getAttribute('aria-checked')) !== String(enabled)) {
      await toggle.click();
      const confirm = admin.getByRole('dialog').getByRole('button', { name: enabled ? 'Switch on' : 'Switch off', exact: true });
      if (await confirm.isVisible().catch(() => false)) await confirm.click();
    }
    await expect(toggle).toHaveAttribute('aria-checked', String(enabled));
  } finally {
    await admin.close();
  }
}

async function download(page: Page, name: string | RegExp) {
  const file = page.waitForEvent('download');
  await page.getByRole('button', { name }).click();
  const d = await file;
  const bytes = await (await import('node:fs/promises')).readFile((await d.path())!);
  expect(bytes.subarray(0, 5).toString()).toBe('%PDF-');
  await d.saveAs(test.info().outputPath(d.suggestedFilename())); // kept with the run's results, to look at the print
  return { name: d.suggestedFilename(), sha: createHash('sha256').update(bytes).digest('hex') };
}

test('billing: switch on, open the drawer, bill a walk-in, split payment, print bill and receipt, close the day with a second person', async ({ page, browser }) => {
  test.setTimeout(300_000);
  const watch = watchUrls(page);
  const patient = syntheticPatient('Meena', 'Raghavan');

  await test.step('the practice admin switches billing on in Settings (#1430)', async () => {
    await setBilling(browser, true);
  });

  await test.step('the receptionist opens the drawer with a ₹1,000 float', async () => {
    await signIn(page, joseph);
    await page.goto('/app/billing/day-close');
    await page.getByLabel('Opening float').fill('1000');
    await page.getByRole('button', { name: 'Open drawer' }).click();
    await expect(page.getByText('Expected in drawer').first()).toBeVisible();
    await expectNoAxeViolations(page);
  });

  let visitId = '';
  await test.step('a walk-in patient is registered and checked in with Dr. Arjun Rao', async () => {
    const visit = page.waitForResponse(r => r.request().method() === 'POST' && new URL(r.url()).pathname === '/v1/appointments/walk-in');
    await walkIn(page, patient, /Dr\. Arjun Rao/);
    visitId = (await (await visit).json() as { id: string }).id;
    expect(visitId).toBeTruthy();
  });

  await test.step('the visit is billed at the doctor\'s new-visit price and issued', async () => {
    await page.goto(`/app/billing/visit/${visitId}`); // the board's "Take payment" link, which shows on the Billing card
    await page.getByRole('row', { name: /Consultation/ }).getByRole('button', { name: 'Confirm' }).click();
    await page.getByRole('button', { name: 'Issue bill' }).click();
    await expect(page.getByRole('heading', { name: /^Bill INV\// })).toBeVisible();
    await expect(page.getByText('₹500.00').first()).toBeVisible(); // the seeded new-visit fee (SyntheticSeed.SaveWithFeesAsync)
    await expectNoAxeViolations(page);
  });

  await test.step('the patient pays ₹200 cash and the rest by card', async () => {
    await page.getByLabel('Amount (INR)').fill('200');
    await page.getByRole('button', { name: 'Record payment' }).click();
    await expect(page.getByText('Partly paid')).toBeVisible();
    await page.getByRole('combobox', { name: 'Method' }).click();
    await page.getByRole('option', { name: "Card (on the clinic's terminal)" }).click();
    await page.getByRole('button', { name: 'Use the balance' }).click();
    await page.getByLabel('Terminal reference').fill('TID 0042 APPR 7781');
    await page.getByRole('button', { name: 'Record payment' }).click();
    await expect(page.getByText('Paid', { exact: true }).first()).toBeVisible();
  });

  await test.step('the bill prints on A5 and the receipt on the 80 mm roll; a re-print is the identical stored PDF (MDV-P4-005)', async () => {
    const bill = await download(page, 'Bill PDF');
    expect(bill.name).toBe('bill-a5.pdf');
    expect((await download(page, 'Bill PDF')).sha).toBe(bill.sha);
    await page.getByRole('group', { name: 'Paper' }).getByRole('button', { name: '80 mm roll' }).click();
    const receipt = await download(page, 'Receipt PDF');
    expect(receipt.name).toBe('receipt-thermal.pdf');
    expect((await download(page, 'Receipt PDF')).sha).toBe(receipt.sha);
  });

  await test.step('the drawer is counted ₹100 short: a note, then the receptionist cannot confirm her own count', async () => {
    await page.goto('/app/billing/day-close');
    await expect(page.getByText('₹1,200.00').first()).toBeVisible(); // ₹1,000 float + ₹200 cash; the card part is not in the drawer
    await page.getByLabel('₹500.00').fill('2');
    await page.getByLabel('₹100.00').fill('1'); // ₹1,100 counted
    await expect(page.getByText('Attention · cash short ₹100.00')).toBeVisible();
    await page.getByLabel('Variance note').fill('₹100 change given from personal cash');
    await page.getByRole('button', { name: 'Send for confirmation' }).click();
    await expect(page.getByRole('button', { name: 'Confirm and close day' })).toHaveCount(0);
  });

  await test.step('the practice admin confirms on her own account; the closed day prints its shift report', async () => {
    const admin = await browser.newPage();
    try {
      await signIn(admin, lakshmi);
      await admin.goto('/app/billing/day-close');
      await admin.getByRole('button', { name: 'Confirm and close day' }).click();
      const row = admin.getByRole('row', { name: /A\. Joseph/ }).first();
      await expect(row).toBeVisible();
      const file = admin.waitForEvent('download');
      await row.getByRole('button', { name: /Shift report/ }).click();
      const report = await file;
      await report.saveAs(test.info().outputPath(report.suggestedFilename()));
      const bytes = await (await import('node:fs/promises')).readFile((await report.path())!);
      expect(bytes.subarray(0, 5).toString()).toBe('%PDF-');
      await expectNoAxeViolations(admin);
    } finally {
      await admin.close();
    }
  });

  await test.step('the bill and day close are right to left and axe-clean in Arabic', async () => {
    await expectArabicAxeClean(browser, joseph, `/app/billing/visit/${visitId}`);
    await expectArabicAxeClean(browser, joseph, '/app/billing/day-close');
  });

  // No patient detail in any URL the browser asked for.
  expect(watch.leaks([patient.name.slice(patient.name.indexOf(' ') + 1), patient.mobile.replace(/\s/g, '').slice(-5), patient.dob])).toEqual([]);
});
