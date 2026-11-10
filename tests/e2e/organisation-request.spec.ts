import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P1-062 against a mocked API (the rules, the state machine, RLS and the approval API are in OrganisationRequestTests on real PostgreSQL).
// J1 step 1: after sign-up the owner opens the request from the shell notice, adds each document, sends it, and sees it wait for Medvya.
// Synthetic cast only; the files are one-line stand-ins.
const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
async function axe(page: Page) {
  await page.addScriptTag({ path: axePath });
  return page.evaluate(async () =>
    (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
      .map(v => `${v.id} (${v.nodes.length})`));
}

type Doc = { id: string; kind: string; contentType: string; sizeBytes: number; uploadedAt: string };
const clinicDocs = ['RegistrationCertificate', 'TaxRegistration', 'OwnerId', 'MedicalCouncilRegistration'];
const hospitalDocs = ['HospitalRegistration', 'TaxRegistration', 'OwnerId', 'SignatoryLetter', 'Dpa'];

async function mockRequest(page: Page, { type = 'Clinic', state = 'Draft', reason = null as string | null } = {}) {
  const required = type === 'Clinic' ? clinicDocs : hospitalDocs;
  const api = { state, documents: [] as Doc[], submits: 0, starts: 0, contact: null as { name: string; email: string } | null };
  await page.route('**/v1/me/context', r => r.fulfill({ json: {
    user: { id: 'u-arjun', displayName: 'Arjun Rao (synthetic)' },
    memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Rao Clinic (synthetic)', facilityId: 'f-1', facilityName: 'Rao Clinic (synthetic)', role: 'SoloDoctor' }],
    active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'SoloDoctor', flags: [], country: 'IN', organisationState: api.state, capabilities: { StaffRolesIntegrations: 'Config' } },
  } }));
  await page.route('**/v1/settings/**', r => r.fulfill({ json: {} }));
  await page.route('**/v1/organisation/request', r => r.fulfill({ json: {
    type, state: api.state, reason, itContactName: api.contact?.name ?? null, itContactEmail: api.contact?.email ?? null, submittedAt: null,
    requiredDocuments: required, documents: api.documents,
  } }));
  await page.route('**/v1/organisation/request/documents/*', r => {
    const kind = new URL(r.request().url()).pathname.split('/').pop()!;
    const doc = { id: `d-${kind}`, kind, contentType: 'application/pdf', sizeBytes: 12, uploadedAt: '2026-11-10T04:00:00Z' };
    api.documents = [...api.documents.filter(d => d.kind !== kind), doc];
    return r.fulfill({ json: doc });
  });
  await page.route('**/v1/organisation/request/it-contact', r => { api.contact = r.request().postDataJSON() as { name: string; email: string }; return r.fulfill({ status: 204 }); });
  await page.route('**/v1/organisation/request/start-new', r => { api.starts++; api.state = 'Draft'; api.documents = []; return r.fulfill({ status: 204 }); });
  await page.route('**/v1/organisation/request/submit', r => { api.submits++; api.state = 'Submitted'; return r.fulfill({ status: 204 }); });
  return api;
}

const pdf = { name: 'document.pdf', mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.4 synthetic') };

test('the owner opens the request from the notice, adds every document and sends it (en)', async ({ page }) => {
  const api = await mockRequest(page);
  await page.goto('/settings');
  await page.getByRole('note').getByRole('link', { name: 'Open the request.' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Onboarding request' })).toBeVisible();
  const send = page.getByRole('button', { name: 'Send request to Medvya' });
  await expect(send).toBeDisabled();
  await expect(page.getByText('Add every document (and, for a hospital, the IT contact) to send the request.')).toBeVisible();

  await page.getByLabel('Clinic registration certificate').setInputFiles(pdf);
  await expect(page.getByRole('listitem').filter({ hasText: 'Clinic registration certificate' }).getByText('Added')).toBeVisible();
  await expect(send).toBeDisabled();
  expect(await axe(page)).toEqual([]);
  for (const label of ['Tax registration (GSTIN or TRN)', /^Owner’s government ID/, 'Your medical council registration']) await page.getByLabel(label).setInputFiles(pdf);
  await expect(send).toBeEnabled();

  await send.click();
  await expect.poll(() => api.submits).toBe(1);
  await expect(page.getByRole('status').filter({ hasText: 'Sent. Waiting for Medvya to review it.' })).toBeVisible();
  await expect(send).toHaveCount(0); // locked: no more edits while Medvya reviews
  await expect(page.getByLabel('Clinic registration certificate')).toHaveCount(0);
  expect(await axe(page)).toEqual([]);
});

test('a file of the wrong type is refused before it is sent, and the reason is announced', async ({ page }) => {
  const api = await mockRequest(page);
  await page.goto('/organisation');
  await page.getByLabel('Clinic registration certificate').setInputFiles({ name: 'logo.svg', mimeType: 'image/svg+xml', buffer: Buffer.from('<svg/>') });
  await expect(page.getByRole('alert').filter({ hasText: 'Use a PDF, PNG or JPEG file.' })).toBeVisible();
  expect(api.documents).toHaveLength(0);
});

test('a rejected owner can start a new request, which opens empty for editing', async ({ page }) => {
  const api = await mockRequest(page, { state: 'Rejected', reason: 'Not a registered clinic' });
  await page.goto('/organisation');
  await expect(page.getByText('Not a registered clinic')).toBeVisible();
  await expect(page.getByLabel('Clinic registration certificate')).toHaveCount(0); // final for this request
  expect(await axe(page)).toEqual([]);
  await page.getByRole('button', { name: 'Start a new request' }).click();
  await expect.poll(() => api.starts).toBe(1);
  await expect(page.getByLabel('Clinic registration certificate')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Start a new request' })).toHaveCount(0);
});

test('Medvya asked for changes: the reason is shown and the request can be sent again', async ({ page }) => {
  const api = await mockRequest(page, { state: 'NeedsChanges', reason: 'Registration certificate is unreadable' });
  api.documents = clinicDocs.map(kind => ({ id: `d-${kind}`, kind, contentType: 'application/pdf', sizeBytes: 12, uploadedAt: '2026-11-10T04:00:00Z' }));
  await page.goto('/organisation');
  await expect(page.getByText('Registration certificate is unreadable')).toBeVisible();
  await expect(page.getByText('Medvya asked for changes. Fix what the reason says and send it again.')).toBeVisible();
  await page.getByLabel('Clinic registration certificate').setInputFiles(pdf); // replace the unreadable one
  await page.getByRole('button', { name: 'Send again to Medvya' }).click();
  await expect.poll(() => api.submits).toBe(1);
});

test('a hospital also gives its IT contact, with errors under the field', async ({ page }) => {
  const api = await mockRequest(page, { type: 'Hospital' });
  await page.goto('/organisation');
  await expect(page.getByLabel('Authorised signatory letter')).toBeAttached();
  await expect(page.getByLabel('Signed data processing agreement')).toBeAttached();
  await page.getByLabel('Email', { exact: true }).fill('not an address');
  await page.getByRole('button', { name: 'Save contact' }).click();
  await expect(page.locator('#it-name-error')).toHaveText('This is required.');
  await expect(page.locator('#it-email-error')).toHaveText('Check the format.');
  await page.getByLabel('Name', { exact: true }).fill('Sanjay Verma (synthetic)');
  await page.getByLabel('Email', { exact: true }).fill('it@hospital.example');
  await page.getByRole('button', { name: 'Save contact' }).click();
  await expect.poll(() => api.contact).toEqual({ name: 'Sanjay Verma (synthetic)', email: 'it@hospital.example' });
  await expect(page.getByText('Contact saved.')).toBeVisible();
});

test('the request page is right to left and accessible in Arabic', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('medvya.locale', 'ar'));
  await mockRequest(page, { state: 'NeedsChanges', reason: 'شهادة التسجيل غير مقروءة' });
  await page.goto('/organisation');
  await expect(page.getByRole('heading', { level: 1, name: 'طلب الانضمام' })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.dir)).toBe('rtl');
  await expect(page.getByText('شهادة التسجيل غير مقروءة')).toBeVisible();
  expect(await axe(page)).toEqual([]);
});
