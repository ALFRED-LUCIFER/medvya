import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P1-008. CI has no API: the staff endpoints are mocked with the synthetic cast (docs/00-overview/personas.md).
// The server-side rules (403 for other roles, custom roles capped at practice admin, audit) are tested on SQL Server in StaffTests.cs.
const facility = '11111111-1111-4111-8111-111111111111';
const admin = {
  user: { id: 'u-lakshmi', displayName: 'K. Lakshmi (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: facility, facilityName: 'Adyar', role: 'PracticeAdmin' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: facility, role: 'PracticeAdmin', flags: [], capabilities: { StaffRolesIntegrations: 'Config', RegisterBookPatients: 'Full' } },
};
const capabilities = ['RegisterBookPatients', 'CheckInQueue', 'InvoicesPayments', 'Patient360Chart', 'VitalsAllergiesEntry', 'ClinicalNotes', 'PrescriptionsOrders',
  'RecordingConsentCapture', 'AiDraftsEvidence', 'ResultReviewAcknowledge', 'CareTasksPatientContact', 'ReleaseRecordsToPortal', 'DevicePairingFleet', 'StaffRolesIntegrations', 'AuditLog'];
const receptionist: Record<string, string> = { RegisterBookPatients: 'Full', CheckInQueue: 'Full', InvoicesPayments: 'Full' };
const roles = {
  builtIn: ['Receptionist', 'Nurse', 'CareCoordinator', 'PracticeAdmin'].map(role => ({
    role,
    rows: capabilities.map(capability => ({ capability, access: role === 'Receptionist' ? (receptionist[capability] ?? 'None') : 'None', extra: capability === 'AuditLog' ? 'Read' : null })),
  })),
  custom: [{ id: 'r-1', name: 'Desk with audit (synthetic)', baseRole: 'Receptionist', capabilities: ['AuditLog'] }],
};
const member = (over: object) => ({
  membershipId: 'x', name: null, email: null, role: 'Receptionist', customRoleId: null, customRoleName: null, facilityId: facility, facilityName: 'Adyar (synthetic)',
  status: 'Active', mfa: 'enrolled', invitationId: null, invitationState: null, invitationExpiresAt: null, ...over,
});
const staff = [
  member({ membershipId: 'a', name: 'K. Lakshmi (synthetic)', role: 'PracticeAdmin' }),
  member({ membershipId: 'b', email: 'joseph@example.test', status: 'Invited', mfa: 'pending', invitationId: 'i-1', invitationState: 'pending', invitationExpiresAt: '2026-11-12T10:00:00Z' }),
  member({ membershipId: 'c', email: 'revoked@example.test', status: 'Invited', mfa: 'pending', invitationId: 'i-3', invitationState: 'revoked', invitationExpiresAt: '2026-11-12T10:00:00Z' }),
];

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
async function axe(page: Page) {
  await page.addScriptTag({ path: axePath });
  return page.evaluate(async () =>
    (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
      .map(v => `${v.id} (${v.nodes.length})`));
}

async function setUp(page: Page, locale = 'en') {
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: admin }));
  await page.route('**/v1/settings/locations', r => r.fulfill({ json: [{ id: facility, name: 'Adyar (synthetic)', address: null, timeZone: 'Asia/Kolkata' }] }));
  await page.route('**/v1/staff', r => r.request().method() === 'GET' ? r.fulfill({ json: staff }) : r.fulfill({ status: 201, json: { id: 'i-2', expiresAt: '2026-11-12T10:00:00Z' } }));
  await page.route('**/v1/staff/roles', r => r.fulfill({ json: roles }));
}

test('staff table shows role, location, second factor and status, with resend and revoke for a pending invitation', async ({ page }) => {
  await setUp(page);
  await page.goto('/settings?tab=staff');
  const row = page.getByRole('row', { name: /joseph@example\.test/ });
  await expect(row).toContainText('Receptionist');
  await expect(row).toContainText('Invited');
  await expect(row).toContainText('Not yet');
  await expect(page.getByRole('row', { name: /K\. Lakshmi/ })).toContainText('Set up');
  await expect(row.getByRole('button', { name: 'Resend invitation to joseph@example.test' })).toBeVisible();
  await expect(row.getByRole('button', { name: 'Revoke invitation to joseph@example.test' })).toBeVisible();
  // MDV-P1-052: a revoked invitation reads as revoked in the status column, not as Invited, and cannot be revoked again.
  const gone = page.getByRole('row', { name: /revoked@example\.test/ });
  await expect(gone.getByRole('cell').nth(3)).toHaveText('Invitation revoked');
  await expect(gone.getByRole('button', { name: /Revoke/ })).toHaveCount(0);

  let revoked = false;
  await page.route('**/v1/invitations/i-1', r => { revoked = r.request().method() === 'DELETE'; return r.fulfill({ status: 204 }); });
  await row.getByRole('button', { name: /Revoke/ }).click();
  await expect(page.getByText('Invitation revoked. The link no longer works.')).toBeVisible();
  expect(revoked).toBe(true);
  expect(await axe(page)).toEqual([]);
});

test('choosing Doctor shows the redirect notice and cannot continue', async ({ page }) => {
  await setUp(page);
  await page.goto('/settings?tab=staff');
  await page.getByRole('button', { name: 'Add staff member' }).click();
  await page.getByRole('radio', { name: /^Doctor/ }).check();
  await expect(page.getByText('Doctors need licence verification. Use Settings → Doctors → Add doctor.')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Next' })).toBeDisabled();
});

test('adding a receptionist: role, location, locked permissions, then the invitation is sent', async ({ page }) => {
  await setUp(page);
  let body = '';
  await page.route('**/v1/staff', r => {
    if (r.request().method() !== 'POST') return r.fallback();
    body = r.request().postData() ?? '';
    return r.fulfill({ status: 201, json: { id: 'i-2', expiresAt: '2026-11-12T10:00:00Z' } });
  });
  await page.goto('/settings?tab=staff');
  await page.getByRole('button', { name: 'Add staff member' }).click();
  await page.getByRole('radio', { name: /^Receptionist/ }).check();
  await page.getByRole('button', { name: 'Next' }).click();
  await page.getByLabel('Work email').fill('new.desk@example.test');
  await page.getByRole('button', { name: 'Next' }).click();
  await expect(page.getByRole('listitem').filter({ hasText: 'Clinical notes' })).toContainText('no access');
  await expect(page.getByRole('listitem').filter({ hasText: 'Register / book patients' })).toContainText('full');
  await expect(page.getByRole('switch')).toHaveCount(0); // locked rows are displayed, not editable
  expect(await axe(page)).toEqual([]);
  await page.getByRole('button', { name: 'Send invitation', exact: true }).click();
  await expect(page.getByText('Invitation sent.')).toBeVisible();
  expect(JSON.parse(body)).toEqual({ email: 'new.desk@example.test', facilityId: facility, role: 'Receptionist', customRoleId: null });
});

test('a custom role offers only the rows the server allows, and Arabic lays the table out right to left', async ({ page }) => {
  await setUp(page, 'ar');
  await page.goto('/settings?tab=staff');
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await page.getByRole('button', { name: 'إضافة دور مخصص' }).click();
  await expect(page.getByRole('switch')).toHaveCount(1); // only the audit log row is addable
  expect(await axe(page)).toEqual([]);
});

test('offboarding asks for a reason, confirms, and says the last administrator cannot go', async ({ page }) => {
  await setUp(page);
  const desk = member({ membershipId: 'c', name: 'A. Joseph (synthetic)' });
  await page.route('**/v1/staff', r => r.request().method() === 'GET' ? r.fulfill({ json: [staff[0], desk] }) : r.fallback());
  let body = '';
  await page.route('**/v1/staff/c/offboard', r => { body = r.request().postData() ?? ''; return r.fulfill({ json: { openVisits: 0 } }); });
  await page.route('**/v1/staff/a/offboard', r => r.fulfill({ status: 400, json: { errors: { membershipId: ['last_admin'] } } }));
  await page.goto('/settings?tab=staff');
  await page.getByRole('button', { name: 'Offboard A. Joseph (synthetic)' }).click();
  await page.getByRole('button', { name: 'Offboard and sign out everywhere' }).click();
  await expect(page.getByText('Enter a reason.')).toBeVisible();
  await page.getByLabel('Reason (kept in the audit trail)').fill('Left the practice');
  expect(await axe(page)).toEqual([]);
  await page.getByRole('button', { name: 'Offboard and sign out everywhere' }).click();
  await expect(page.getByText('Offboarded. Their access has ended and their sessions are signed out.')).toBeVisible();
  expect(JSON.parse(body)).toEqual({ reason: 'Left the practice', offerRebooking: false });
  await expect(page.getByText(/Patients already checked in/)).toHaveCount(0);

  await page.getByRole('button', { name: 'Offboard K. Lakshmi (synthetic)' }).click();
  await page.getByLabel('Reason (kept in the audit trail)').fill('Testing');
  await page.getByRole('button', { name: 'Offboard and sign out everywhere' }).click();
  // scoped: the page can also show an unrelated 'settings could not be loaded' alert while its request fails
  await expect(page.getByRole('alert').filter({ hasText: 'last administrator' })).toBeVisible();
});

test('offboarding a doctor asks what happens to their patients and names the ones waiting today (#225)', async ({ page }) => {
  await setUp(page);
  const doctor = member({ membershipId: 'd', userId: 'u-d', name: 'Dr. R. Menon (synthetic)', role: 'HospitalDoctor' });
  await page.route('**/v1/staff', r => r.request().method() === 'GET' ? r.fulfill({ json: [staff[0], doctor] }) : r.fallback());
  let body = '';
  await page.route('**/v1/staff/d/offboard', r => { body = r.request().postData() ?? ''; return r.fulfill({ json: { openVisits: 2 } }); });
  await page.goto('/settings?tab=staff');
  await page.getByRole('button', { name: 'Offboard Dr. R. Menon (synthetic)' }).click();
  await page.getByLabel('Reason (kept in the audit trail)').fill('Left the practice');
  await page.getByRole('button', { name: 'Offboard and sign out everywhere' }).click();
  await expect(page.getByText('Choose what happens to their booked patients.')).toBeVisible();
  expect(body).toBe('');
  await page.getByLabel(/Offer new slots/).check();
  expect(await axe(page)).toEqual([]);
  await page.getByRole('button', { name: 'Offboard and sign out everywhere' }).click();
  await expect(page.getByText('Patients already checked in for them: 2. Reassign them to another doctor on the board.')).toBeVisible();
  expect(JSON.parse(body)).toEqual({ reason: 'Left the practice', rebooking: undefined, offerRebooking: true });
});

test('an offboarded person is brought back with a reason; a role above the admin is refused (#225)', async ({ page }) => {
  await setUp(page);
  const gone = member({ membershipId: 'g', userId: 'u-g', name: 'A. Joseph (synthetic)', status: 'Offboarded' });
  await page.route('**/v1/staff', r => r.request().method() === 'GET' ? r.fulfill({ json: [staff[0], gone] }) : r.fallback());
  let body = '';
  await page.route('**/v1/staff/g/reactivate', r => { body = r.request().postData() ?? ''; return r.fulfill({ status: 204 }); });
  await page.goto('/settings?tab=staff');
  await expect(page.getByRole('button', { name: 'Offboard A. Joseph (synthetic)' })).toHaveCount(0);
  await page.getByRole('button', { name: 'Bring back A. Joseph (synthetic)' }).click();
  await page.getByRole('button', { name: 'Bring back', exact: true }).click();
  await expect(page.getByText('Enter a reason.')).toBeVisible();
  await page.getByLabel('Reason (kept in the audit trail)').fill('Returned from leave');
  expect(await axe(page)).toEqual([]);
  await page.getByRole('button', { name: 'Bring back', exact: true }).click();
  await expect(page.getByText('Brought back. They can sign in again with the same role.')).toBeVisible();
  expect(JSON.parse(body)).toEqual({ reason: 'Returned from leave' });

  await page.unroute('**/v1/staff/g/reactivate');
  await page.route('**/v1/staff/g/reactivate', r => r.fulfill({ status: 400, json: { errors: { membershipId: ['above_granter'] } } }));
  await page.getByRole('button', { name: 'Bring back A. Joseph (synthetic)' }).click();
  await page.getByLabel('Reason (kept in the audit trail)').fill('Again');
  await page.getByRole('button', { name: 'Bring back', exact: true }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'access you do not have yourself' })).toBeVisible();
});

test('the offboarding form lays out right to left in Arabic', async ({ page }) => {
  await setUp(page, 'ar');
  await page.route('**/v1/staff', r => r.request().method() === 'GET' ? r.fulfill({ json: [member({ membershipId: 'c', name: 'A. Joseph (synthetic)' })] }) : r.fallback());
  await page.goto('/settings?tab=staff');
  await page.getByRole('button', { name: 'إنهاء الخدمة لـ A. Joseph (synthetic)' }).click();
  await expect(page.getByLabel('السبب (يُحفظ في سجل التدقيق)')).toBeVisible();
  expect(await axe(page)).toEqual([]);
});

// MDV-P0-030: the shared Table. Sorting is keyboard-operable, sets aria-sort and reorders the rows; the name column starts at the reading edge in Arabic.
for (const locale of ['en', 'ar'])
  test(`sorting the staff table by keyboard sets aria-sort and reorders the rows (${locale})`, async ({ page }) => {
    await setUp(page, locale);
    await page.goto('/settings?tab=staff');
    const table = page.getByRole('table');
    const nameSort = table.locator('th[aria-sort]').first().getByRole('button');
    const firstRow = () => table.locator('tbody tr').first().locator('th').innerText();
    await expect(table.locator('th[aria-sort="none"]')).toHaveCount(2);
    expect((await nameSort.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    await nameSort.focus();
    await page.keyboard.press('Enter');
    await expect(table.locator('th[aria-sort="ascending"]')).toHaveCount(1);
    const ascending = await firstRow();
    await page.keyboard.press('Enter');
    await expect(table.locator('th[aria-sort="descending"]')).toHaveCount(1);
    expect(await firstRow()).not.toBe(ascending);
    const [header, box] = [await table.locator('th').first().boundingBox(), await table.boundingBox()];
    if (locale === 'ar') expect(header!.x + header!.width).toBeGreaterThan(box!.x + box!.width - 60);
    else expect(header!.x).toBeLessThan(box!.x + 60);
    expect(await axe(page)).toEqual([]);
  });
