import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P1-064, J1 step 1 (Medvya approves the organisation): the console's approvals queue and detail against a mocked console API with synthetic
// organisations. The server rules (roles, reason, state machine, DPA, audit in both logs, the review login's narrow rights) are tested on PostgreSQL
// in PlatformApprovalTests.cs; here the browser path is real: filters, the private viewer, the reason, the email preview, read-only roles, Arabic.
test.use({ baseURL: 'http://localhost:4175' });

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
async function axe(page: Page) {
  await page.addScriptTag({ path: axePath });
  return page.evaluate(async () =>
    (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: { html: string }[] }[] }> } }).axe.run()).violations
      .map(v => `${v.id} (${v.nodes.length}) ${v.nodes.map(n => n.html.slice(0, 160)).join(' | ')}`));
}

const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==', 'base64');
const me = (role: string) => ({ displayName: 'Ops Reviewer (synthetic)', role, keyVerified: true, hasKey: true, canRegister: false });
const doc = (id: string, kind: string) => ({ id, kind, contentType: 'image/png', sizeBytes: 2048, uploadedAt: '2026-11-08T05:00:00Z' });
const clinicDocs = [doc('d1', 'RegistrationCertificate'), doc('d2', 'TaxRegistration'), doc('d3', 'OwnerId'), doc('d4', 'MedicalCouncilRegistration')];
const hospitalDocs = [doc('h1', 'HospitalRegistration'), doc('h2', 'TaxRegistration'), doc('h3', 'OwnerId'), doc('h4', 'SignatoryLetter')]; // no Dpa

type Org = { id: string; name: string; type: 'Clinic' | 'Hospital'; state: string; docs: ReturnType<typeof doc>[]; tax: string };
async function mock(page: Page, role: string) {
  const orgs: Org[] = [
    { id: 'o-clinic', name: 'Rao Clinic (synthetic)', type: 'Clinic', state: 'Submitted', docs: clinicDocs, tax: '27AAPFU0939F1ZV' },
    { id: 'o-hospital', name: 'Lakeview Hospital (synthetic)', type: 'Hospital', state: 'Submitted', docs: hospitalDocs, tax: '27AAPFU0939F1ZV' },
    { id: 'o-done', name: 'Menon Clinic (synthetic)', type: 'Clinic', state: 'Approved', docs: clinicDocs, tax: '12345' },
  ];
  const api = { posts: [] as { path: string; body: { reason: string } }[], opened: [] as string[], filters: [] as string[] };
  await page.route('**/v1/me', r => r.fulfill({ json: me(role) }));
  await page.route('**/v1/organisations/?filter=*', r => {
    const filter = new URL(r.request().url()).searchParams.get('filter')!;
    api.filters.push(filter);
    const keep = (o: Org) => filter === 'All' || (filter === 'Waiting' ? o.state === 'Submitted' : filter === 'NeedsChanges' ? o.state === 'NeedsChanges' : ['Approved', 'Rejected', 'Suspended'].includes(o.state));
    return r.fulfill({ json: orgs.filter(keep).map(o => ({ tenantId: o.id, name: o.name, country: 'IN', type: o.type, state: o.state, submittedAt: '2026-11-07T05:00:00Z', reason: null, itContactName: null, itContactEmail: null, documents: o.docs })) });
  });
  await page.route(/\/v1\/organisations\/o-[a-z]+$/, r => {
    const o = orgs.find(x => r.request().url().endsWith(x.id))!;
    return r.fulfill({ json: {
      name: o.name, country: 'IN', type: o.type, state: o.state, submittedAt: '2026-11-07T05:00:00Z', reason: null, address: '4 Lake Road, Surat', contactEmail: 'desk@example.test',
      registrationNumber: 'REG-1001', taxNumber: o.tax, itContactName: o.type === 'Hospital' ? 'IT lead (synthetic)' : null, itContactEmail: o.type === 'Hospital' ? 'it@example.test' : null,
      documents: o.docs, history: o.state === 'Approved' ? [{ at: '2026-11-08T06:00:00Z', actor: 'Ops Reviewer (synthetic)', state: 'Approved', reason: null }] : [],
    } });
  });
  await page.route('**/v1/organisations/*/documents/*', r => { api.opened.push(r.request().url().split('/').pop()!); return r.fulfill({ body: png, contentType: 'image/png' }); });
  await page.route('**/v1/organisations/*/*', r => {
    if (r.request().method() !== 'POST') return r.fallback();
    const body = r.request().postDataJSON() as { reason: string };
    api.posts.push({ path: new URL(r.request().url()).pathname, body });
    const approve = r.request().url().endsWith('/approve');
    return r.fulfill({ json: approve
      ? { subject: 'Rao Clinic (synthetic) is approved on Medvya', body: 'Rao Clinic (synthetic) is approved. Sign in to Medvya to register patients and book visits.' }
      : { subject: 'Medvya needs changes to the request for Rao Clinic (synthetic)', body: `Reason: ${body.reason}` } });
  });
  return api;
}

test('a reviewer opens a request, reads a document in the private viewer, approves with a reason and sees the email', async ({ page }) => {
  const api = await mock(page, 'Reviewer');
  await page.goto('/approvals');
  await expect(page.getByRole('heading', { name: 'Organisation approvals' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Rao Clinic (synthetic)' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Menon Clinic (synthetic)' })).toHaveCount(0); // Waiting is the default filter
  await page.getByRole('button', { name: 'Rao Clinic (synthetic)' }).click();
  await expect(page).toHaveURL(/\/approvals\/o-clinic$/);
  await expect(page.getByText('Tax number format is valid.')).toBeVisible();
  await expect(page.getByText('Names and addresses are not checked automatically')).toBeVisible();

  await page.getByRole('button', { name: 'Open Clinic registration certificate' }).click();
  const viewer = page.getByRole('region', { name: 'Document viewer' });
  await expect(viewer.getByRole('img', { name: 'Document viewer' })).toBeVisible();
  await expect(viewer.locator('[aria-hidden][style*="data:image/svg+xml"]')).toBeAttached(); // the watermark layer
  await expect(viewer.getByRole('link')).toHaveCount(0); // no download link
  expect(api.opened).toEqual(['d1']);
  expect(await axe(page)).toEqual([]);
  await viewer.getByRole('button', { name: 'Close viewer' }).click();
  await expect(viewer).toHaveCount(0);

  await page.getByRole('button', { name: 'Approve', exact: true }).click();
  await expect(page.getByLabel('Reason (saved in the audit log)')).toHaveValue('Documents checked and in order.'); // preset wording, editable
  await page.getByLabel('Reason (saved in the audit log)').fill('no');
  await page.getByRole('button', { name: 'Approve and email the owner' }).click();
  await expect(page.getByText('Give a reason of at least 5 characters.')).toBeVisible();
  expect(api.posts).toEqual([]);
  await page.getByLabel('Reason (saved in the audit log)').fill('Registration certificate and GSTIN checked');
  await page.getByRole('button', { name: 'Approve and email the owner' }).click();
  await expect(page.getByText('Done. The decision is logged and the applicant was emailed.')).toBeVisible();
  await expect(page.getByText('Rao Clinic (synthetic) is approved on Medvya')).toBeVisible();
  expect(api.posts).toEqual([{ path: '/v1/organisations/o-clinic/approve', body: { reason: 'Registration certificate and GSTIN checked' } }]);
  expect(await axe(page)).toEqual([]);
});

test('asking for changes sends the reason; the filters list each state', async ({ page }) => {
  const api = await mock(page, 'Operations');
  await page.goto('/approvals/o-clinic');
  await page.getByRole('button', { name: 'Ask for changes' }).click();
  await page.getByLabel('Reason (saved in the audit log)').fill('The tax registration page is cut off');
  await page.getByRole('button', { name: 'Send reason and ask for changes' }).click();
  await expect(page.getByText('Reason: The tax registration page is cut off')).toBeVisible();
  expect(api.posts[0]).toEqual({ path: '/v1/organisations/o-clinic/request-changes', body: { reason: 'The tax registration page is cut off' } });

  await page.getByRole('tab', { name: 'Decided' }).click();
  await expect(page.getByRole('button', { name: 'Menon Clinic (synthetic)' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Rao Clinic (synthetic)' })).toHaveCount(0);
  await page.getByRole('tab', { name: 'All' }).click();
  for (const name of ['Rao Clinic (synthetic)', 'Lakeview Hospital (synthetic)', 'Menon Clinic (synthetic)']) await expect(page.getByRole('button', { name })).toBeVisible();
  expect(api.filters).toEqual(expect.arrayContaining(['Waiting', 'Decided', 'All']));
});

test('a hospital without its signed DPA cannot be approved: the button is off and asking for changes is offered', async ({ page }) => {
  await mock(page, 'Reviewer');
  await page.goto('/approvals/o-hospital');
  await expect(page.getByText('IT lead (synthetic)', { exact: false })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Approve', exact: true })).toBeDisabled();
  await expect(page.getByText('Can’t approve yet: this hospital hasn’t uploaded its signed data processing agreement.')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Ask for changes' })).toBeEnabled();
});

test('suspend and reinstate are offered on approved and suspended organisations only', async ({ page }) => {
  await mock(page, 'Reviewer');
  await page.goto('/approvals/o-done');
  await expect(page.getByRole('button', { name: 'Approve', exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Suspend organisation…' }).click();
  await expect(page.getByText('Existing records stay readable for care under way.')).toBeVisible();
});

for (const role of ['Support', 'Finance']) {
  test(`${role} reads requests and documents but has no decision buttons`, async ({ page }) => {
    const api = await mock(page, role);
    await page.goto('/approvals/o-clinic');
    await expect(page.getByRole('heading', { name: 'Rao Clinic (synthetic)' })).toBeVisible();
    await expect(page.getByText('Your role can read requests but not decide them.')).toBeVisible();
    for (const name of ['Approve', 'Ask for changes', 'Reject', 'Suspend organisation…', 'Reinstate organisation'])
      await expect(page.getByRole('button', { name })).toHaveCount(0);
    await page.getByRole('button', { name: 'Open Clinic registration certificate' }).click();
    await expect(page.getByRole('img', { name: 'Document viewer' })).toBeVisible();
    expect(api.posts).toEqual([]);
    expect(await axe(page)).toEqual([]);
  });
}

test('the queue and a request read in Arabic, right to left, axe clean', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('medvya.locale', 'ar'));
  await mock(page, 'Reviewer');
  await page.goto('/approvals/o-clinic');
  await expect(page.getByRole('heading', { name: 'موافقات المنظمات' })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.dir)).toBe('rtl');
  await expect(page.getByRole('button', { name: 'موافقة', exact: true })).toBeVisible();
  await expect(page.getByRole('tab', { name: 'قيد الانتظار' })).toBeVisible();
  expect(await axe(page)).toEqual([]);
  await page.getByRole('button', { name: 'موافقة', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'الموافقة على هذه المنظمة' })).toBeVisible();
  expect(await axe(page)).toEqual([]);
});
