import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';
import { authorised, line, mockPrescription } from './support/prescription';

// MDV-P2-017 (E-05, E-10): authorising is its own action after the checks (the server asks for the step-up first); only an authorised, unchanged
// prescription prints, in English plus the chosen language (Arabic right to left); the doctor approves the layout; the QR check is public and
// shows validity only. CI has no API: endpoints are mocked in the server's shapes. The gates, the stored PDF and the audit are proven in RxAuthorisationTests.
const metformin = line('rx-1', 'Metformin', { dose: '1', doseUnit: 'tablet', route: 'oral', frequency: 'BD', sig: { en: 'Take 1 tablet oral BD.', hi: '', ta: '', ar: '' } });

test('authorising asks for the step-up, then the prescription prints with the licence and English plus Tamil or Arabic', async ({ page }) => {
  const { calls } = await mockPrescription(page, { lines: [metformin], reauthFirst: true });
  await page.goto('/consult/e-1');
  const rx = page.getByRole('region', { name: 'Prescription' });
  await expect(rx).toContainText('Authorising is separate from signing the note.');
  await rx.getByRole('button', { name: 'Authorise prescription' }).click();
  await expect(rx.getByRole('alert')).toContainText('Confirm your identity to authorise. What you entered stays here.');
  await expect(rx.getByRole('button', { name: 'Confirm identity' })).toBeVisible();

  await rx.getByRole('button', { name: 'Authorise prescription' }).click(); // back from the step-up
  await expect(rx).toContainText('Authorised · version 1 · RX-3F9A21C7');
  expect(calls.authorised).toBe(2);
  await rx.getByRole('link', { name: 'Print prescription' }).click();

  const paper = page.getByRole('article');
  await expect(paper.getByRole('heading', { name: 'Iyer Family Clinic · Adyar' })).toBeVisible();
  await expect(paper).toContainText('Dr. Arjun Rao (synthetic) · MBBS, MD (General Medicine)');
  await expect(paper).toContainText(/Licence \W?84213\W? · Tamil Nadu Medical Council/); // the number is isolated left to right
  await expect(paper).toContainText('000001');
  await expect(paper).toContainText('Schedule H drug. Warning');
  await expect(paper).toContainText('Stop: Atorvastatin');
  await expect(paper.getByRole('img', { name: 'QR code to check this prescription is valid' })).toBeVisible();
  await expect(paper.locator('[lang="ta"]')).toHaveCount(0); // English only until a second language is chosen

  const pick = async (option: string) => {
    await page.getByRole('combobox', { name: 'Second language' }).click();
    await page.getByRole('option', { name: option, exact: true }).click();
  };
  await pick('Tamil');
  await expect(paper.locator('[lang="en"]')).toHaveText('Take 1 tablet by mouth twice a day after food for 5 days. Quantity: 10.');
  await expect(paper.locator('[lang="ta"]')).toContainText('மாத்திரை');
  await expect(page.getByText('The PDF is in English. To give the patient the instructions in Tamil too, print this page.')).toBeVisible();
  await pick('Arabic');
  await expect(paper.locator('[lang="ar"]')).toHaveAttribute('dir', 'rtl');

  const layout = page.getByRole('region', { name: 'Your prescription layout' });
  await layout.getByRole('button', { name: 'Approve this layout as mine' }).click();
  await expect(layout).toContainText('You approved layout 1 on');
  expect(calls.approved).toBe(1);
});

test('a draft changed since it was authorised must be authorised again before it prints', async ({ page }) => {
  await mockPrescription(page, { lines: [metformin], authorisation: { authorised: authorised(), changed: true } });
  await page.goto('/consult/e-1');
  const rx = page.getByRole('region', { name: 'Prescription' });
  await expect(rx).toContainText('Changed since it was authorised. Authorise the changes before printing.');
  await expect(rx.getByRole('button', { name: 'Authorise the changes' })).toBeVisible();
  await expect(rx.getByRole('link', { name: 'Print prescription' })).toHaveCount(0);
  await page.goto('/consult/e-1/rx/print'); // the server refuses it too
  await expect(page.getByRole('alert')).toContainText('Only an authorised prescription prints.');
});

const verified = { status: 'Valid', number: 'RX-3F9A21C7', versionNo: 1, authorisedAt: '2026-10-19T04:50:00Z', timeZone: 'Asia/Kolkata', clinic: 'Iyer Family Clinic · Adyar', doctor: 'Dr. Arjun Rao (synthetic)', lines: 2 };

test('the QR check shows validity only, and an unknown code says not to dispense', async ({ page }) => {
  const tokens: string[] = [];
  await page.route('**/v1/prescriptions/verify', r => {
    const { token } = r.request().postDataJSON() as { token: string };
    tokens.push(token);
    return token === 'synthetic-token' ? r.fulfill({ json: verified }) : r.fulfill({ status: 404, json: {} });
  });
  await page.goto('/verify/rx/synthetic-token');
  await expect(page.getByRole('status').filter({ hasText: 'Valid prescription' })).toBeVisible();
  await expect(page.getByText('Issued by Dr. Arjun Rao (synthetic), Iyer Family Clinic · Adyar, on')).toBeVisible();
  await expect(page.getByText('2 medicines')).toBeVisible();
  await expect(page.getByText('It never shows the patient or the medicines.')).toBeVisible();
  await page.goto('/verify/rx/made-up');
  await expect(page.getByRole('alert')).toContainText('This code does not match a prescription. Do not dispense from it without checking with the clinic.');
  expect(tokens).toEqual(['synthetic-token', 'made-up']); // in the body, never a query string
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
const axeViolations = async (page: Page) => {
  await page.addScriptTag({ path: axePath });
  return page.evaluate(async () =>
    (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
      .map(v => `${v.id} (${v.nodes.length})`));
};

for (const locale of ['en', 'ar'])
  test(`axe finds no violations on the authorise step, the print preview at 200% zoom and the QR check (${locale})`, async ({ page }) => {
    await mockPrescription(page, { locale, lines: [metformin], reauthFirst: true });
    await page.goto('/consult/e-1');
    if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    const rx = page.getByRole('region', { name: locale === 'ar' ? 'الوصفة الطبية' : 'Prescription' });
    await rx.getByRole('button', { name: locale === 'ar' ? 'اعتماد الوصفة' : 'Authorise prescription' }).click(); // the step-up alert in words
    await expect(rx.getByRole('button', { name: locale === 'ar' ? 'تأكيد الهوية' : 'Confirm identity' })).toBeVisible();
    expect(await axeViolations(page)).toEqual([]);

    // WCAG 1.4.10: 200% zoom on a 1280 px screen leaves 640 CSS px; the preview reflows with no sideways scrolling and keeps its labels.
    await rx.getByRole('button', { name: locale === 'ar' ? 'اعتماد الوصفة' : 'Authorise prescription' }).click();
    await page.setViewportSize({ width: 640, height: 800 });
    await rx.getByRole('link', { name: locale === 'ar' ? 'طباعة الوصفة' : 'Print prescription' }).click();
    await expect(page.getByRole('article')).toContainText('000001');
    await expect(page.getByRole('img', { name: locale === 'ar' ? 'رمز QR للتحقق من صلاحية هذه الوصفة' : 'QR code to check this prescription is valid' })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    for (const target of await page.locator('main button, main a').all()) expect((await target.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    expect(await axeViolations(page)).toEqual([]);

    await page.route('**/v1/prescriptions/verify', r => r.fulfill({ json: verified }));
    await page.goto('/verify/rx/synthetic-token');
    await expect(page.getByRole('heading', { name: locale === 'ar' ? 'التحقق من الوصفة' : 'Prescription check' })).toBeVisible();
    expect(await axeViolations(page)).toEqual([]);
  });
