import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P2-006: documents (E-03). CI has no API or Blob: endpoints are mocked in the shapes the server sends. The scan gate, the five-minute link
// that fetches the exact original, the front desk's refusal, immutability and the audit rows are covered by DocumentTests on PostgreSQL and Azurite.
const session = (role: string, caps: Record<string, string>) => ({
  user: { id: 'u-1', displayName: role === 'Receptionist' ? 'Lakshmi Desk (synthetic)' : 'Dr. Arjun Rao (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role, flags: [] as string[], country: 'IN', capabilities: caps },
});
const doctor = session('SoloDoctor', { RegisterBookPatients: 'Full', CheckInQueue: 'Full', Patient360Chart: 'Full' });
const desk = session('Receptionist', { RegisterBookPatients: 'Full', CheckInQueue: 'Full', Patient360Chart: 'None' });
const patient = { fullName: 'Meera Iyer (synthetic)', mrn: '000001', dateOfBirth: '1972-03-12' };
const row = (id: string, type: string, scanStatus: string, source = 'Imported') =>
  ({ id, type, documentDate: '2026-10-01', source, scanStatus, contentType: 'application/pdf', sizeBytes: 2048, uploadedBy: 'Dr. Arjun Rao (synthetic)', uploadedAt: '2026-10-02T04:30:00Z' });
const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==', 'base64');

type Options = { as?: typeof doctor; locale?: string; canOpen?: boolean; docs?: ReturnType<typeof row>[]; uploads?: { status: number; json: unknown }[]; path?: string };
async function open(page: Page, { as = doctor, locale = 'en', canOpen = true, docs = [row('d1', 'LabReport', 'Clean')], uploads = [], path = '/patients/p-1/docs' }: Options = {}) {
  const calls = { posts: [] as { url: string; body: Buffer; csrf: string | undefined }[], links: 0, lists: 0, patches: [] as { body: unknown; csrf: string | undefined }[] };
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: as }));
  await page.route(/\/v1\/patients\/p-1\/documents(\?.*)?$/, r => {
    if (r.request().method() === 'POST') {
      calls.posts.push({ url: r.request().url(), body: r.request().postDataBuffer()!, csrf: r.request().headers()['x-medvya-csrf'] });
      const next = uploads.shift() ?? { status: 200, json: row('d-new', 'LabReport', 'Clean') };
      return r.fulfill(next);
    }
    calls.lists++;
    return r.fulfill({ json: { patient, documents: docs, canOpen, timeZone: 'Asia/Kolkata' } });
  });
  await page.route('**/v1/documents/d1/view-link', r => {
    calls.links++;
    return r.fulfill({ json: { url: 'https://blob.example.test/clinical-documents/abc?sp=r&se=2026-10-02T04:35:00Z', expiresAt: new Date(Date.now() + 5 * 60_000).toISOString(), contentType: 'image/png', viewer: 'Dr. Arjun Rao (synthetic)', viewedAt: new Date().toISOString() } });
  });
  await page.route('**/v1/documents/d1', r => {
    calls.patches.push({ body: r.request().postDataJSON(), csrf: r.request().headers()['x-medvya-csrf'] });
    return r.fulfill({ json: { ...row('d1', 'Imaging', 'Clean'), documentDate: '2026-09-15' } });
  });
  await page.route('https://blob.example.test/**', r => r.fulfill({ body: png, contentType: 'image/png' }));
  await page.goto(path);
  return calls;
}

test('a doctor sees each document with type, date, source and scan state, and opens a clean one in the watermarked viewer', async ({ page }) => {
  await page.clock.install();
  const calls = await open(page, { docs: [row('d1', 'LabReport', 'Clean'), row('d2', 'Imaging', 'Pending', 'PatientReported'), row('d3', 'Other', 'Infected')] });
  await page.clock.resume();
  const list = page.getByRole('region', { name: 'Documents in the record' });
  await expect(list.getByRole('listitem')).toHaveCount(3);
  const first = list.getByRole('listitem').first();
  await expect(first).toContainText('Lab report');
  await expect(first).toContainText('Dated 01 Oct 2026');
  await expect(first).toContainText('Imported');
  await expect(first).toContainText('Scan passed');
  await expect(list.getByRole('listitem').nth(1)).toContainText('Patient reported');
  await expect(list.getByRole('listitem').nth(1)).toContainText('Scanning');
  await expect(list.getByRole('listitem').nth(2)).toContainText('Quarantined');
  // Only the clean one can be opened.
  await expect(list.getByRole('button', { name: /^Open/ })).toHaveCount(1);

  await list.getByRole('button', { name: 'Open the Lab report' }).click();
  const viewer = page.getByRole('region', { name: 'Document viewer' });
  await expect(viewer.getByRole('img', { name: 'The document page' })).toBeVisible();
  // The watermark over the page carries who opened it and when (a background tile, so the text is in the style, not in the page text).
  await expect(viewer.locator('div[aria-hidden]')).toHaveAttribute('style', /Dr\.%20Arjun%20Rao%20\(synthetic\).*2026/);
  await expect(viewer).toContainText('Opened for you only.');
  expect(calls.links).toBe(1);

  // The link lives five minutes: after that the viewer closes by itself and says why.
  await page.clock.fastForward('05:05');
  await expect(viewer).toHaveCount(0);
  await expect(page.getByText('The viewer closed because its link expired. Open the document again.')).toBeVisible();
});

test('the front desk uploads a file and sees the list but has no way to open anything', async ({ page }) => {
  const calls = await open(page, { as: desk, canOpen: false, path: '/patients/p-1/docs' });
  await expect(page.getByText('The front desk can add documents and see this list, but cannot open them.')).toBeVisible();
  await expect(page.getByRole('button', { name: /^Open/ })).toHaveCount(0);
  await page.getByLabel('Choose a file (PDF, JPEG or PNG)').setInputFiles({ name: 'scan.pdf', mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.4\nsynthetic') });
  await expect(page.getByText('Added. The security scan passed.')).toBeVisible();
  expect(calls.posts).toHaveLength(1);
  const url = new URL(calls.posts[0]!.url);
  expect(url.searchParams.get('type')).toBe('LabReport');
  expect(url.searchParams.get('source')).toBe('Imported');
  expect(url.searchParams.get('date')).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  expect(url.toString()).not.toContain('scan.pdf'); // no file name in the URL
  expect(calls.posts[0]!.body.toString()).toBe('%PDF-1.4\nsynthetic'); // the file as the raw body
  expect(calls.posts[0]!.csrf).toBe('1');
});

test('a doctor edits a document\'s type and date; the desk has no edit button', async ({ page }) => {
  const calls = await open(page);
  await page.getByRole('button', { name: 'Edit details: Lab report' }).click();
  const form = page.getByRole('group', { name: 'Edit document details' });
  await expect(form).toContainText('The original file stays as it was');
  await form.getByRole('combobox', { name: 'Type' }).click();
  await page.getByRole('option', { name: 'Imaging' }).click();
  await form.getByLabel('Date on the document').fill('2026-09-15');
  await form.getByRole('button', { name: 'Save' }).click();
  await expect(form).toHaveCount(0);
  expect(calls.patches).toEqual([{ body: { type: 'Imaging', documentDate: '2026-09-15' }, csrf: '1' }]);
  const deskCalls = await open(page, { as: desk, canOpen: false });
  expect(deskCalls.patches).toHaveLength(0);
  await expect(page.getByRole('button', { name: /^Edit/ })).toHaveCount(0);
});

test('a failed upload keeps the file and can be retried', async ({ page }) => {
  const calls = await open(page, { uploads: [{ status: 500, json: {} }] });
  await page.getByLabel('Choose a file (PDF, JPEG or PNG)').setInputFiles({ name: 'a.pdf', mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.4\nretry me') });
  const alert = page.getByRole('alert').filter({ hasText: 'The upload did not work' });
  await expect(alert).toBeVisible();
  await alert.getByRole('button', { name: 'Try again' }).click();
  await expect(page.getByText('Added. The security scan passed.')).toBeVisible();
  expect(calls.posts.map(p => p.body.toString())).toEqual(['%PDF-1.4\nretry me', '%PDF-1.4\nretry me']);
});

test('a file that is not a pdf, jpeg or png is refused before it is sent; an infected one is blocked', async ({ page }) => {
  const calls = await open(page, { uploads: [{ status: 200, json: row('d-bad', 'LabReport', 'Infected') }] });
  await page.getByLabel('Choose a file (PDF, JPEG or PNG)').setInputFiles({ name: 'x.html', mimeType: 'text/html', buffer: Buffer.from('<html>') });
  await expect(page.getByRole('alert').filter({ hasText: 'Only PDF, JPEG and PNG files can be added.' })).toBeVisible();
  expect(calls.posts).toHaveLength(0);
  await page.getByRole('button', { name: 'Dismiss' }).click();
  await page.getByLabel('Choose a file (PDF, JPEG or PNG)').setInputFiles({ name: 'y.pdf', mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.4\nbad') });
  await expect(page.getByRole('alert').filter({ hasText: 'put in quarantine' })).toBeVisible();
});

test('several camera pages are combined into one PDF', async ({ page }) => {
  const calls = await open(page);
  const camera = page.getByLabel('Add another page').or(page.getByLabel('Scan with the camera')).first();
  await page.locator('input[capture]').setInputFiles({ name: 'p1.png', mimeType: 'image/png', buffer: png });
  await expect(page.getByText('1 pages captured')).toBeVisible();
  await page.locator('input[capture]').setInputFiles({ name: 'p2.png', mimeType: 'image/png', buffer: png });
  await expect(page.getByText('2 pages captured')).toBeVisible();
  await expect(camera).toBeDefined();
  await page.getByRole('button', { name: 'Save 2 pages as one PDF' }).click();
  await expect(page.getByText('Added. The security scan passed.')).toBeVisible();
  expect(calls.posts).toHaveLength(1);
  const body = calls.posts[0]!.body.toString('latin1');
  expect(body.startsWith('%PDF-')).toBe(true);
  expect(body).toContain('/Count 2');
  await expect(page.getByText('2 pages captured')).toHaveCount(0); // the pages are in the record now
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const locale of ['en', 'ar'])
  test(`axe finds no violations with the viewer open (${locale}); 44 px targets`, async ({ page }) => {
    await open(page, { locale });
    await page.getByRole('button', { name: /^(Open|فتح)/ }).click();
    await expect(page.getByRole('region', { name: locale === 'ar' ? 'عارض المستندات' : 'Document viewer' })).toBeVisible();
    if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    for (const b of await page.getByRole('main').getByRole('button').all()) expect((await b.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations.map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
  });
