import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P0-013. CI has no API: invitation endpoints are mocked with the synthetic cast (docs/00-overview/personas.md).
// Built at run time (64 base64url characters), so no token-shaped literal sits in the repository.
const token = 'synthetic-invitation'.padEnd(64, '0');
const matrix: [string, string][] = [
  ['RegisterBookPatients', 'Full'], ['CheckInQueue', 'Full'], ['InvoicesPayments', 'Full'], ['Patient360Chart', 'None'],
  ['VitalsAllergiesEntry', 'None'], ['ClinicalNotes', 'None'], ['PrescriptionsOrders', 'None'], ['RecordingConsentCapture', 'None'],
  ['AiDraftsEvidence', 'None'], ['ResultReviewAcknowledge', 'None'], ['CareTasksPatientContact', 'None'], ['ReleaseRecordsToPortal', 'None'],
  ['DevicePairingFleet', 'None'], ['StaffRolesIntegrations', 'None'], ['AuditLog', 'None'],
];
const preview = {
  clinic: 'Iyer Family Clinic (synthetic)', facility: 'Adyar (synthetic)', inviter: 'Dr. Arjun Rao (synthetic)', role: 'Receptionist',
  expiresAt: '2026-10-28T10:00:00Z', licenceVerificationRequired: false,
  capabilities: matrix.map(([capability, access]) => ({ capability, access })),
};
const admin = {
  user: { id: 'u-lakshmi', displayName: 'K. Lakshmi' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Adyar', role: 'PracticeAdmin' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'PracticeAdmin', flags: [], capabilities: { StaffRolesIntegrations: 'Config' } },
};

async function setUp(page: Page, invitation: object | null, locale = 'en') {
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ status: 401 }));
  await page.route('**/v1/invitations/preview', r => r.fulfill(invitation ? { json: invitation } : { status: 404 }));
}

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
async function axe(page: Page) {
  await page.addScriptTag({ path: axePath });
  return page.evaluate(async () =>
    (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
      .map(v => `${v.id} (${v.nodes.length})`));
}

test('invitee sees the clinic, role, capabilities and facility, and Join starts sign-in carrying the invitation', async ({ page }) => {
  await setUp(page, preview);
  let posted = '';
  await page.route('**/v1/invitations/preview', r => { posted = r.request().postData() ?? ''; return r.fulfill({ json: preview }); });
  await page.route('**/bff/login**', r => r.fulfill({ body: 'signing in' }));
  await page.goto(`/invite/${token}`);

  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Join Iyer Family Clinic (synthetic)');
  await expect(page.getByText('Dr. Arjun Rao (synthetic) invited you as Receptionist.')).toBeVisible();
  const items = page.getByRole('listitem');
  await expect(items).toHaveCount(15);
  await expect(items.first()).toHaveText('Allowed: Register / book patients');
  await expect(items.filter({ hasText: 'Clinical notes' })).toHaveText('Not allowed: Clinical notes');
  await expect(page.getByText('Facility: Adyar (synthetic)')).toBeVisible();
  expect(JSON.parse(posted)).toEqual({ token }); // the token goes in the body, not a URL

  await page.getByRole('button', { name: 'Sign in & join' }).click();
  await expect(page).toHaveURL(`/bff/login?returnUrl=%2F&invitation=${token}`);
});

test('an expired, revoked or used link shows the expired state and nothing to join', async ({ page }) => {
  await setUp(page, null);
  await page.goto(`/invite/${token}`);
  await expect(page.getByRole('alert')).toContainText('Ask your admin to resend the invitation.');
  await expect(page.getByRole('button', { name: 'Sign in & join' })).toHaveCount(0);
});

test('admin invites a colleague by email with role and facility', async ({ page }) => {
  await page.route('**/v1/me/context', r => r.fulfill({ json: admin }));
  await page.route('**/v1/invitations/facilities', r => r.fulfill({ json: [{ id: '5e1d0001-0001-4000-8000-000000000001', name: 'Adyar (synthetic)' }] }));
  let sent: unknown;
  await page.route('**/v1/invitations', r => {
    sent = r.request().postDataJSON();
    return r.fulfill({ status: 201, json: { id: 'i-1', expiresAt: '2026-10-28T10:00:00Z', licenceVerificationRequired: true } });
  });
  await page.goto('/admin/staff');

  await page.getByRole('button', { name: 'Send invitation' }).click();
  await expect(page.getByText('Enter one valid email address.')).toBeVisible();
  await expect(page.getByText('Choose a facility.')).toBeVisible();

  await page.getByLabel('Email').fill('joseph@example.test');
  await page.getByRole('combobox', { name: 'Role' }).click();
  await page.getByRole('option', { name: 'Hospital doctor' }).click();
  await page.getByRole('combobox', { name: 'Facility' }).click();
  await page.getByRole('option', { name: 'Adyar (synthetic)' }).click();
  await page.getByRole('button', { name: 'Send invitation' }).click();

  await expect(page.getByText('Invitation sent. It expires 28 Oct 2026.')).toBeVisible();
  await expect(page.getByText('This doctor must verify their licence before recording or signing.')).toBeVisible();
  expect(sent).toEqual({ email: 'joseph@example.test', role: 'HospitalDoctor', facilityId: '5e1d0001-0001-4000-8000-000000000001' });
  expect(await axe(page)).toEqual([]);
});

for (const locale of ['en', 'ar'])
  test(`axe finds no violations on the invitation page (${locale})`, async ({ page }) => {
    await setUp(page, { ...preview, licenceVerificationRequired: true }, locale);
    await page.goto(`/invite/${token}`);
    // The page itself, not the loading fallback of the lazy route (which also has a heading).
    await expect(page.getByRole('button', { name: locale === 'ar' ? 'سجّل الدخول وانضم' : 'Sign in & join' })).toBeVisible();
    if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    expect(await axe(page)).toEqual([]);
  });
