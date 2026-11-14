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

const verified = { status: 'Valid', number: 'RX-3F9A21C7', versionNo: 1, authorisedAt: '2026-10-19T04:50:00Z', timeZone: 'Asia/Kolkata', clinic: 'Iyer Family Clinic · Adyar', doctor: 'Dr. Arjun Rao (synthetic)', lines: 2, contentHash: 'a'.repeat(64) };

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
  await expect(page.getByText('SHA-256 of the authorised lines: ' + 'a'.repeat(64))).toBeVisible(); // moved here from the patient's copy
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
    await expect(page.getByRole('article')).not.toContainText('a'.repeat(64)); // the hash is on the QR page, not the patient's copy
    if (locale === 'en') await expect(page.getByRole('article')).toContainText('Age 53');
    if (locale === 'en') await expect(page.getByRole('article')).toContainText('Diagnosis: Type 2 diabetes (synthetic)');
    await expect(page.getByRole('img', { name: locale === 'ar' ? 'رمز QR للتحقق من صلاحية هذه الوصفة' : 'QR code to check this prescription is valid' })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    for (const target of await page.locator('main button, main a').all()) expect((await target.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    expect(await axeViolations(page)).toEqual([]);

    await page.route('**/v1/prescriptions/verify', r => r.fulfill({ json: verified }));
    await page.goto('/verify/rx/synthetic-token');
    await expect(page.getByRole('heading', { name: locale === 'ar' ? 'التحقق من الوصفة' : 'Prescription check' })).toBeVisible();
    expect(await axeViolations(page)).toEqual([]);
  });

// MDV-P2-047: the step-up remembers only the word "authorise"; back on the page Authorise is in view and focused and the status says what to
// press, but nothing is authorised until the doctor presses it. The provider is stubbed to send the browser straight back to the return address.
for (const locale of ['en', 'ar'])
  test(`back from the step-up, Authorise is in view and focused and waits for the press (${locale})`, async ({ page }) => {
    const { calls } = await mockPrescription(page, { locale, lines: [metformin], reauthFirst: true });
    const asked: { returnUrl: string }[] = [];
    await page.route('**/bff/reauth', r => {
      const body = r.request().postDataJSON() as { returnUrl: string };
      asked.push(body);
      return r.fulfill({ json: { location: body.returnUrl } });
    });
    await page.goto('/consult/e-1');
    const rx = page.getByRole('region', { name: locale === 'ar' ? 'الوصفة الطبية' : 'Prescription' });
    const authorise = rx.getByRole('button', { name: locale === 'ar' ? 'اعتماد الوصفة' : 'Authorise prescription' });
    await authorise.click();
    await rx.getByRole('button', { name: locale === 'ar' ? 'تأكيد الهوية' : 'Confirm identity' }).click();

    await expect(authorise).toBeFocused();
    await expect(authorise).toBeInViewport();
    const said = locale === 'ar' ? 'تم تأكيد الهوية. اضغط «اعتماد الوصفة».' : 'Identity confirmed. Press Authorise prescription.';
    await expect(rx.getByRole('status').filter({ hasText: said })).toBeVisible();
    expect(asked).toEqual([{ returnUrl: '/consult/e-1?resume=authorise' }]); // the opaque path and one fixed word, nothing else
    await expect(page).toHaveURL(/\/consult\/e-1$/); // the word leaves the address, so a reload says nothing
    expect(await axeViolations(page)).toEqual([]);
    expect(calls.authorised).toBe(1); // only the refused try before the step-up: coming back authorised nothing

    await page.keyboard.press('Enter');
    await expect(rx).toContainText(locale === 'ar' ? 'معتمدة · الإصدار 1' : 'Authorised · version 1');
    expect(calls.authorised).toBe(2);
    await expect(rx.getByRole('status').filter({ hasText: said })).toHaveCount(0);
  });

test('back from the step-up for cancelling, Cancel prescription is focused and nothing is cancelled', async ({ page }) => {
  await mockPrescription(page, { lines: [metformin], authorisation: { authorised: authorised(1), changed: false } });
  let cancels = 0;
  page.on('request', r => { if (r.method() !== 'GET' && r.url().includes('/prescription/')) cancels++; });
  await page.goto('/consult/e-1?resume=authorise');
  const rx = page.getByRole('region', { name: 'Prescription' });
  await expect(rx.getByRole('button', { name: 'Cancel prescription' })).toBeFocused();
  await expect(rx.getByRole('status').filter({ hasText: 'Identity confirmed. Press Cancel prescription.' })).toBeVisible();
  expect(cancels).toBe(0);
});

test('a click before Authorise appears keeps the focus where the doctor put it', async ({ page }) => {
  await mockPrescription(page, { lines: [metformin] });
  let release = () => {};
  const held = new Promise<void>(r => { release = r; });
  await page.route('**/v1/encounters/e-1/prescription', async r => { await held; return r.fallback(); });
  await page.goto('/consult/e-1?resume=authorise');
  await page.getByRole('region', { name: 'Patient identity' }).click();
  release();
  const authorise = page.getByRole('region', { name: 'Prescription' }).getByRole('button', { name: 'Authorise prescription' });
  await expect(authorise).toBeVisible();
  await expect(authorise).not.toBeFocused();
  await expect(page.getByText('Identity confirmed. Press Authorise prescription.')).toHaveCount(0);
});

test('a word that is not on the list in the return address moves nothing and says nothing', async ({ page }) => {
  await mockPrescription(page, { lines: [metformin] });
  await page.goto('/consult/e-1?resume=meera');
  const authorise = page.getByRole('region', { name: 'Prescription' }).getByRole('button', { name: 'Authorise prescription' });
  await expect(authorise).toBeVisible();
  await expect(authorise).not.toBeFocused();
  await expect(page.getByText('Identity confirmed. Press Authorise prescription.')).toHaveCount(0);
});
