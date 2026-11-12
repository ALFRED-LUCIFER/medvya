import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';
import { mockOrders } from './support/orders';
import { openPart } from './support/disclosure';

// MDV-P2-018 (E-05): the doctor composes investigation orders as drafts, switches one off, places the rest on purpose, and prints the slip.
// CI has no API: endpoints are mocked in the shapes the server sends. Drafts-until-placed, one audit row per call, no ordering by anything
// else, the timeline and the access checks are proven on PostgreSQL in OrderTests and OrderEndpointTests.
const orderRow = (page: Page, name: string) => page.getByRole('row', { name: new RegExp(name) });

test('orders stay drafts until Place orders, a switched-off test is not placed, and the slip shows the placed ones', async ({ page }) => {
  const { calls } = await mockOrders(page);
  await page.goto('/consult/e-1');
  await openPart(page, 'Orders');
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

  // The server's copy (MDV-P2-018): saved under a name with no patient data; a 422 says to print instead.
  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Download PDF' }).click();
  expect((await download).suggestedFilename()).toBe('investigation-slip.pdf');
  calls.pdfStatus = 422;
  await page.getByRole('button', { name: 'Download PDF' }).click();
  await expect(page.getByRole('alert')).toHaveText('This paper has text the PDF cannot show yet. Print this page instead.');
  expect(calls.pdfs).toBe(2);
});

// The QR code on the slip or letter opens this public page: what the paper is and from whom, never whose (PaperPdfTests prove the server side).
async function mockPaperCheck(page: Page, locale = 'en', known = true) {
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/papers/verify', r => known
    ? r.fulfill({ json: { kind: 'InvestigationSlip', issuedAt: '2026-10-19T04:50:00Z', timeZone: 'Asia/Kolkata', clinic: 'Iyer Family Clinic · Adyar', doctor: 'Dr. Arjun Rao (synthetic)', items: 2 } })
    : r.fulfill({ status: 404, json: {} }));
}

test('the paper check says the slip is genuine, from whom and how many tests, and an unknown code says to check with the clinic', async ({ page }) => {
  await mockPaperCheck(page);
  await page.goto('/verify/paper/synthetic-token');
  await expect(page.getByRole('heading', { name: 'Document check' })).toBeVisible();
  await expect(page.getByText('Genuine investigation slip')).toBeVisible();
  await expect(page.getByText('Issued by Dr. Arjun Rao (synthetic), Iyer Family Clinic · Adyar')).toBeVisible();
  await expect(page.getByText('2 tests')).toBeVisible();
  await expect(page.getByText('Meera')).toHaveCount(0);

  await page.unrouteAll();
  await mockPaperCheck(page, 'en', false);
  await page.reload();
  await expect(page.getByRole('alert')).toHaveText('This code does not match a document from a clinic. Check with the clinic before acting on it.');
});

test('an order set adds its tests as drafts', async ({ page }) => {
  const { calls } = await mockOrders(page);
  await page.goto('/consult/e-1');
  await openPart(page, 'Orders');
  await page.getByRole('combobox', { name: 'Order set' }).click();
  await page.getByRole('option', { name: 'Diabetes review labs' }).click();
  await page.getByRole('button', { name: 'Add its tests' }).click();
  expect(calls.sets).toEqual([{ orderSetId: 's-1' }]);
  await expect(orderRow(page, 'Fasting glucose')).toContainText('Draft');
});

test('an ended consultation shows its orders and cannot change them', async ({ page }) => {
  await mockOrders(page, { ended: true });
  await page.goto('/consult/e-1');
  await openPart(page, 'Orders');
  await expect(page.getByText('This consultation has ended. Orders can be read and the slip printed, not changed.')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Place orders' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Add test' })).toHaveCount(0);
});

test('orders stay in a closed part of the consultation until the doctor opens it', async ({ page }) => {
  await mockOrders(page);
  await page.goto('/consult/e-1');
  await expect(page.getByRole('region', { name: 'Patient identity' })).toBeVisible();
  await expect(page.getByRole('region', { name: 'Orders' })).toBeHidden();
  await openPart(page, 'Orders');
  await expect(page.getByRole('region', { name: 'Orders' })).toBeVisible();
});

test('orders already placed are open without a click, so none is hidden from the doctor', async ({ page }) => {
  const { orders } = await mockOrders(page);
  orders.push({ id: 'o-1', kind: 'Lab', name: 'HbA1c', note: null, state: 'Ordered', destination: 'Metro Diagnostics', orderedAt: '2026-10-19T04:50:00Z' });
  await page.goto('/consult/e-1');
  await expect(page.getByRole('region', { name: 'Orders' })).toBeVisible();
  await expect(page.getByText('HbA1c')).toBeVisible();
});

test('a role that may not order sees no orders panel', async ({ page }) => {
  await mockOrders(page, { allowed: false });
  await page.goto('/consult/e-1');
  await expect(page.getByRole('region', { name: 'Patient identity' })).toBeVisible();
  await expect(page.getByRole('region', { name: 'Orders' })).toHaveCount(0);
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const locale of ['en', 'ar'])
  test(`axe finds no violations on the orders panel, the slip and the paper check (${locale})`, async ({ page }) => {
    const { orders } = await mockOrders(page, { locale });
    orders.push({ id: 'o-1', kind: 'Lab', name: 'HbA1c', note: 'fasting 10 h', state: 'Ordered', destination: 'Metro Diagnostics', orderedAt: '2026-10-19T04:50:00Z' },
      { id: 'o-2', kind: 'Imaging', name: 'Chest X-ray', note: null, state: 'Draft', destination: null, orderedAt: null });
    await page.route('**/v1/papers/verify', r => r.fulfill({ json: { kind: 'InvestigationSlip', issuedAt: '2026-10-19T04:50:00Z', timeZone: 'Asia/Kolkata', clinic: 'Iyer Family Clinic · Adyar', doctor: 'Dr. Arjun Rao (synthetic)', items: 2 } }));
    for (const path of ['/consult/e-1', '/consult/e-1/slip', '/verify/paper/synthetic-token']) {
      await page.goto(path);
      if (path === '/consult/e-1') await openPart(page, locale === 'ar' ? 'الطلبات' : 'Orders');
      await expect(path.endsWith('slip') ? page.getByRole('article')
        : path.startsWith('/verify') ? page.getByRole('heading', { name: locale === 'ar' ? 'التحقق من المستند' : 'Document check' })
        : page.getByRole('region', { name: locale === 'ar' ? 'الطلبات' : 'Orders' })).toBeVisible();
      if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
      await page.addScriptTag({ path: axePath });
      const violations = await page.evaluate(async () =>
        (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations.map(v => `${v.id} (${v.nodes.length})`));
      expect(violations, path).toEqual([]);
    }
  });
