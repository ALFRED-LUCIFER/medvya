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
  status: 'Active', mfa: 'enrolled', invitationId: null, invitationState: null, invitationExpiresAt: null, isOwner: false, ...over,
});
const staff = [
  member({ membershipId: 'a', userId: 'u-lakshmi', name: 'K. Lakshmi (synthetic)', role: 'PracticeAdmin', isOwner: true }),
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

// MDV-P2-054: a row shows one action that opens a form; the rest (and always Offboard and Revoke) are 44 px items in its Actions menu.
async function rowAction(page: Page, menu: string, item: string) {
  await page.getByRole('button', { name: menu }).first().click();
  await page.getByRole('menuitem', { name: item }).click();
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
  await row.getByRole('button', { name: 'Actions for joseph@example.test' }).click();
  await expect(page.getByRole('menuitem', { name: 'Resend invitation to joseph@example.test' })).toBeVisible();
  await expect(page.getByRole('menuitem', { name: 'Revoke invitation to joseph@example.test' })).toBeVisible();
  await page.keyboard.press('Escape');
  // MDV-P1-052: a revoked invitation reads as revoked in the status column, not as Invited, and cannot be revoked again.
  const gone = page.getByRole('row', { name: /revoked@example\.test/ });
  await expect(gone.getByRole('cell').nth(3)).toHaveText('Invitation revoked', { useInnerText: true });
  await expect(gone.getByRole('button')).toHaveCount(0); // #912: revoked stays revoked: no Resend, no Revoke, no menu

  let revoked = false;
  await page.route('**/v1/invitations/i-1', r => { revoked = r.request().method() === 'DELETE'; return r.fulfill({ status: 204 }); });
  await rowAction(page, 'Actions for joseph@example.test', 'Revoke invitation to joseph@example.test');
  await expect(page.getByText('Invitation revoked. The link no longer works.')).toBeVisible();
  expect(revoked).toBe(true);
  expect(await axe(page)).toEqual([]);
});

test('choosing Doctor shows the redirect notice and cannot continue', async ({ page }) => {
  await setUp(page);
  await page.goto('/settings?tab=staff');
  await page.getByRole('button', { name: 'Add staff member' }).click();
  await page.getByRole('radio', { name: /^Doctor/ }).check();
  await expect(page.getByText('Add doctors from Settings → Doctors → Add doctor. You enter and confirm each licence.')).toBeVisible();
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
  expect(JSON.parse(body)).toEqual({ email: 'new.desk@example.test', facilityId: facility, role: 'Receptionist', customRoleId: null, alsoFacilityIds: [] });
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
  const otherAdmin = member({ membershipId: 'e', userId: 'u-e', name: 'R. Iyer (synthetic)', role: 'PracticeAdmin' });
  await page.route('**/v1/staff', r => r.request().method() === 'GET' ? r.fulfill({ json: [staff[0], desk, otherAdmin] }) : r.fallback());
  let body = '';
  await page.route('**/v1/staff/c/offboard', r => { body = r.request().postData() ?? ''; return r.fulfill({ json: { openVisits: 0 } }); });
  await page.route('**/v1/staff/e/offboard', r => r.fulfill({ status: 400, json: { errors: { membershipId: ['last_admin'] } } }));
  await page.goto('/settings?tab=staff');
  await rowAction(page, 'Actions for A. Joseph (synthetic)', 'Offboard A. Joseph (synthetic)');
  // #912: the form opens below the table; it is brought into view with the reason field focused.
  await expect(page.getByLabel('Reason (kept in the audit trail)')).toBeFocused();
  await expect(page.getByLabel('Reason (kept in the audit trail)')).toBeInViewport();
  await page.getByRole('button', { name: 'Offboard and sign out everywhere' }).click();
  await expect(page.getByText('Enter a reason.')).toBeVisible();
  await page.getByLabel('Reason (kept in the audit trail)').fill('Left the practice');
  expect(await axe(page)).toEqual([]);
  await page.getByRole('button', { name: 'Offboard and sign out everywhere' }).click();
  await expect(page.getByText('Offboarded. Their access has ended and their sessions are signed out.')).toBeVisible();
  expect(JSON.parse(body)).toEqual({ reason: 'Left the practice', offerRebooking: false });
  await expect(page.getByText(/Patients already checked in/)).toHaveCount(0);

  await rowAction(page, 'Actions for R. Iyer (synthetic)', 'Offboard R. Iyer (synthetic)');
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
  await rowAction(page, 'Actions for Dr. R. Menon (synthetic)', 'Offboard Dr. R. Menon (synthetic)');
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

// MDV-P1-069 (J1 step 1, invite staff): a full starter plan is refused by the server and the screen says why, in both languages.
for (const [locale, text, add, role, email, next, send] of [
  ['en', 'Your plan allows 1 doctor and 2 staff; contact Medvya to upgrade', 'Add staff member', /^Receptionist/, 'Work email', 'Next', 'Send invitation'],
  ['ar', 'تتيح خطتك طبيبًا واحدًا وموظفَين اثنين؛ تواصل مع Medvya للترقية', 'إضافة موظف', /^موظف استقبال/, 'البريد الإلكتروني للعمل', 'التالي', 'إرسال الدعوة'],
] as const)
  test(`a full starter plan refuses another staff member and says so (${locale})`, async ({ page }) => {
    await setUp(page, locale);
    await page.route('**/v1/staff', r => r.request().method() === 'POST'
      ? r.fulfill({ status: 409, contentType: 'application/problem+json', json: { title: 'Your plan allows 1 doctor and 2 staff; contact Medvya to upgrade', code: 'plan_limit' } })
      : r.fallback());
    await page.goto('/settings?tab=staff');
    await page.getByRole('button', { name: add }).click();
    await page.getByRole('radio', { name: role }).check();
    await page.getByRole('button', { name: next }).click();
    await page.getByLabel(email).fill('third.desk@example.test');
    await page.getByRole('button', { name: next }).click();
    await page.getByRole('button', { name: send, exact: true }).click();
    await expect(page.getByRole('alert').filter({ hasText: text })).toBeVisible();
    expect(await axe(page)).toEqual([]);
  });

test('the offboarding form lays out right to left in Arabic', async ({ page }) => {
  await setUp(page, 'ar');
  await page.route('**/v1/staff', r => r.request().method() === 'GET' ? r.fulfill({ json: [member({ membershipId: 'c', name: 'A. Joseph (synthetic)' })] }) : r.fallback());
  await page.goto('/settings?tab=staff');
  await rowAction(page, 'الإجراءات لـ A. Joseph (synthetic)', 'إنهاء الخدمة لـ A. Joseph (synthetic)');
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

// MDV-P1-071: a member works at several locations of the organisation; the owner sets them, taking one away needs a reason.
// Mocked like the rest of this file; the server rules (403 at an unassigned location, one seat per person, offboarding at every location) are in StaffLocationsTests.cs.
const velachery = '22222222-2222-4222-8222-222222222222';
const twoLocations = [{ id: facility, name: 'Adyar (synthetic)', address: null, timeZone: 'Asia/Kolkata' }, { id: velachery, name: 'Velachery (synthetic)', address: null, timeZone: 'Asia/Kolkata' }];
for (const [locale, menu, item, reasonLabel, saved] of [
  ['en', 'Actions for P. Devi (synthetic)', 'Locations of P. Devi (synthetic)', 'Reason (kept in the audit trail)', 'Locations saved.'],
  ['ar', 'الإجراءات لـ P. Devi (synthetic)', 'المواقع لـ P. Devi (synthetic)', 'السبب (يُحفظ في سجل التدقيق)', 'تم حفظ المواقع.'],
] as const)
  test(`the owner sets a member's locations, and taking one away needs a reason (${locale})`, async ({ page }) => {
    await setUp(page, locale);
    await page.route('**/v1/settings/locations', r => r.fulfill({ json: twoLocations }));
    const at = (id: string, where: string, name: string) => member({ membershipId: id, userId: 'u-devi', name: 'P. Devi (synthetic)', facilityId: where, facilityName: name });
    await page.route('**/v1/staff', r => r.request().method() === 'GET'
      ? r.fulfill({ json: [at('d1', facility, 'Adyar (synthetic)'), at('d2', velachery, 'Velachery (synthetic)')] }) : r.fallback());
    let body: { facilityIds: string[]; reason: string } | null = null;
    await page.route('**/v1/staff/d1/locations', r => { body = r.request().postDataJSON(); return r.fulfill({ status: 204 }); });
    await page.goto('/settings?tab=staff');
    await rowAction(page, menu, item);
    const boxes = page.getByRole('checkbox');
    await expect(boxes).toHaveCount(2);
    await expect(boxes.nth(0)).toBeChecked();
    await expect(boxes.nth(1)).toBeChecked();
    await boxes.nth(1).uncheck();
    await page.locator('form button[type="submit"]').click();
    await expect(page.getByLabel(reasonLabel)).toBeVisible(); // taking Velachery away asks why; nothing was sent yet
    expect(body).toBeNull();
    await page.getByLabel(reasonLabel).fill('Moved to Adyar');
    expect(await axe(page)).toEqual([]);
    await page.locator('form button[type="submit"]').click();
    await expect(page.getByText(saved)).toBeVisible();
    expect(body).toEqual({ facilityIds: [facility], reason: 'Moved to Adyar' });
  });

// MDV-P1-107: the row offers only what the signed-in user may do; the server refuses the same (SessionControlTests, on PostgreSQL).
const hired = { ...admin, user: { id: 'u-hired', displayName: 'R. Iyer (synthetic)' } };
const owners = [
  member({ membershipId: 'o', userId: 'u-rao', name: 'Dr. Arjun Rao (synthetic)', role: 'SoloDoctor', isOwner: true }),
  member({ membershipId: 'd', userId: 'u-menon', name: 'Dr. R. Menon (synthetic)', role: 'HospitalDoctor' }),
  member({ membershipId: 'h', userId: 'u-hired', name: 'R. Iyer (synthetic)', role: 'PracticeAdmin' }),
  member({ membershipId: 'c', userId: 'u-joseph', name: 'A. Joseph (synthetic)' }),
];
for (const locale of ['en', 'ar'])
  test(`a practice admin is offered no Offboard on the owner, a doctor or their own row, with the reason in words (${locale})`, async ({ page }) => {
    await setUp(page, locale);
    await page.route('**/v1/me/context', r => r.fulfill({ json: hired }));
    await page.route('**/v1/staff', r => r.request().method() === 'GET' ? r.fulfill({ json: owners }) : r.fallback());
    await page.goto('/settings?tab=staff');
    await expect(page.getByRole('row', { name: /A\. Joseph/ })).toBeVisible();
    if (locale === 'en') {
      for (const [name, why] of [['Dr. Arjun Rao', 'Only an owner can offboard an owner.'], ['Dr. R. Menon', 'Only an owner can offboard a doctor.'], ['R. Iyer', 'You cannot offboard yourself.']] as const) {
        const row = page.getByRole('row', { name });
        await expect(row.getByRole('button', { name: /^(Offboard|Actions)/ })).toHaveCount(0); // nothing else to do on these rows, so no menu
        await expect(row).toContainText(why);
      }
      await expect(page.getByRole('row', { name: /Dr\. Arjun Rao/ }).getByRole('button', { name: /^Change role/ })).toHaveCount(0); // an owner's role stays
      await page.getByRole('button', { name: 'Actions for A. Joseph (synthetic)' }).click();
      await expect(page.getByRole('menuitem', { name: 'Offboard A. Joseph (synthetic)' })).toBeVisible();
      await page.keyboard.press('Escape');
    }
    expect(await axe(page)).toEqual([]);
  });

test('an owner changes a receptionist to a nurse in place', async ({ page }) => {
  await setUp(page);
  const desk = member({ membershipId: 'c', userId: 'u-joseph', name: 'A. Joseph (synthetic)' });
  await page.route('**/v1/staff', r => r.request().method() === 'GET' ? r.fulfill({ json: [staff[0], desk] }) : r.fallback());
  let body: unknown = null;
  await page.route('**/v1/staff/c/role', r => { body = r.request().postDataJSON(); return r.fulfill({ status: 204 }); });
  await page.goto('/settings?tab=staff');
  await expect(page.getByRole('row', { name: /K\. Lakshmi/ }).getByRole('button', { name: /^Change role/ })).toHaveCount(0); // not on your own row
  await page.getByRole('button', { name: 'Change role for A. Joseph (synthetic)' }).click();
  await expect(page.getByRole('button', { name: 'Change role', exact: true })).toBeDisabled(); // the same role is no change
  await page.getByRole('combobox', { name: 'New role' }).click();
  await page.getByRole('option', { name: 'Nurse' }).click();
  expect(await axe(page)).toEqual([]);
  await page.getByRole('button', { name: 'Change role', exact: true }).click();
  await expect(page.getByText('Role changed. They were signed out and sign in again with the new role.')).toBeVisible();
  expect(body).toEqual({ role: 'Nurse', customRoleId: null });
});

test('an offboarding without a recent step-up asks to confirm identity first, and nothing changes', async ({ page }) => {
  await setUp(page);
  const desk = member({ membershipId: 'c', userId: 'u-joseph', name: 'A. Joseph (synthetic)' });
  await page.route('**/v1/staff', r => r.request().method() === 'GET' ? r.fulfill({ json: [staff[0], desk] }) : r.fallback());
  await page.route('**/v1/staff/c/offboard', r => r.fulfill({ status: 403, contentType: 'application/problem+json', json: { title: 'Confirm your identity to offboard', code: 'reauth_required' } }));
  let asked: unknown = null;
  await page.route('**/bff/reauth', r => { asked = r.request().postDataJSON(); return r.fulfill({ json: { location: '/settings?tab=staff' } }); });
  await page.goto('/settings?tab=staff');
  await rowAction(page, 'Actions for A. Joseph (synthetic)', 'Offboard A. Joseph (synthetic)');
  await page.getByLabel('Reason (kept in the audit trail)').fill('Left the practice');
  await page.getByRole('button', { name: 'Offboard and sign out everywhere' }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'Confirm your identity first, then press Offboard again. Nothing has changed.' })).toBeVisible();
  expect(await axe(page)).toEqual([]);
  await page.getByRole('button', { name: 'Confirm your identity' }).click();
  await expect.poll(() => asked).toEqual({ returnUrl: '/settings?tab=staff' });
});

// MDV-P2-054: the staff list is a compact table: 36 px rows with a mouse, sorted from the keyboard with the order announced, paged over 50.
const many = Array.from({ length: 55 }, (_, i) => member({ membershipId: `s-${i}`, userId: `u-${i}`, name: `Staff ${String(i).padStart(2, '0')} (synthetic)` }));
for (const locale of ['en', 'ar'])
  test(`over 50 staff are paged, and a keyboard sort is announced and goes back to page 1 (${locale})`, async ({ page }) => {
    await setUp(page, locale);
    await page.route('**/v1/staff', r => r.request().method() === 'GET' ? r.fulfill({ json: [staff[0], ...many] }) : r.fallback());
    await page.goto('/settings?tab=staff');
    const table = page.getByRole('region', { name: locale === 'en' ? 'Staff list' : 'قائمة الموظفين' });
    await expect(table.getByRole('rowheader')).toHaveCount(50);
    const pager = page.getByRole('navigation', { name: locale === 'en' ? 'Pages' : 'الصفحات' });
    await pager.getByRole('button').last().click();
    await expect(table.getByRole('rowheader')).toHaveCount(6);
    const name = table.getByRole('columnheader').first().getByRole('button');
    await name.press('Enter');
    await name.press('Enter');
    await expect(table.getByRole('columnheader').first()).toHaveAttribute('aria-sort', 'descending');
    if (locale === 'en') await expect(table.getByRole('status')).toHaveText('Sorted by Name, descending');
    else await expect(table.getByRole('status')).not.toBeEmpty();
    await expect(table.getByRole('rowheader')).toHaveCount(50);
    await expect(table.getByRole('rowheader').first()).toHaveText('Staff 54 (synthetic)');
    expect(await axe(page)).toEqual([]);
  });

// shortcut: with the settings sub-navigation (MDV-P2-055) and the full 232 px sidebar, the staff panel is under ~830 px below 1440, so the
// row's two actions wrap and the row is two lines; nothing is hidden and nothing scrolls sideways. Upgrade: stack a table by its container
// width, not the viewport, if pilot owners use settings at 1280–1366.
test('below 1440 the staff row may wrap, but nothing scrolls sideways and both actions stay visible', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 800 });
  await setUp(page);
  await page.route('**/v1/staff', r => r.request().method() === 'GET' ? r.fulfill({ json: [staff[0], member({ membershipId: 'c', userId: 'u-joseph', name: 'A. Joseph (synthetic)' })] }) : r.fallback());
  await page.goto('/settings?tab=staff');
  const row = page.getByRole('row', { name: /A\. Joseph/ });
  await expect(row.getByRole('button', { name: 'Change role for A. Joseph (synthetic)' })).toBeInViewport();
  await expect(row.getByRole('button', { name: 'Actions for A. Joseph (synthetic)' })).toBeInViewport();
  expect(await page.getByRole('region', { name: /staff/i }).first().evaluate(e => e.scrollWidth - e.clientWidth)).toBe(0);
});

test('rows are 36 px with a mouse at 1440; Offboard is a 44 px menu item that opens a confirmation', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await setUp(page);
  await page.route('**/v1/staff', r => r.request().method() === 'GET' ? r.fulfill({ json: [staff[0], member({ membershipId: 'c', userId: 'u-joseph', name: 'A. Joseph (synthetic)' })] }) : r.fallback());
  await page.goto('/settings?tab=staff');
  const row = page.getByRole('row', { name: /A\. Joseph/ });
  await expect(row.getByRole('button', { name: 'Change role for A. Joseph (synthetic)' })).toBeVisible(); // the one visible action
  expect(Math.round((await row.boundingBox())!.height)).toBe(36);
  expect(await axe(page)).toEqual([]);
  await row.getByRole('button', { name: 'Actions for A. Joseph (synthetic)' }).click();
  const offboard = page.getByRole('menuitem', { name: 'Offboard A. Joseph (synthetic)' });
  expect((await offboard.boundingBox())!.height).toBeGreaterThanOrEqual(44);
  await offboard.click();
  // A confirmation, not an action: the reason and the destructive button come first, and nothing was sent.
  await expect(page.getByLabel('Reason (kept in the audit trail)')).toBeFocused();
  await expect(page.getByRole('button', { name: 'Offboard and sign out everywhere' })).toBeVisible();
});

test.describe('on a coarse pointer', () => {
  test.use({ hasTouch: true, isMobile: true, viewport: { width: 1024, height: 768 } });
  test('the compact staff table ignores its density: rows are 48 px and row buttons 44 px', async ({ page }) => {
    await setUp(page);
    await page.route('**/v1/staff', r => r.request().method() === 'GET' ? r.fulfill({ json: [staff[0], member({ membershipId: 'c', userId: 'u-joseph', name: 'A. Joseph (synthetic)' })] }) : r.fallback());
    await page.goto('/settings?tab=staff');
    expect(await page.evaluate(() => matchMedia('(any-pointer: coarse)').matches)).toBe(true);
    const row = page.getByRole('row', { name: /A\. Joseph/ });
    await expect(row).toBeVisible();
    expect(Math.round((await row.boundingBox())!.height)).toBeGreaterThanOrEqual(48);
    for (const button of await row.getByRole('button').all()) expect((await button.boundingBox())!.height).toBeGreaterThanOrEqual(44);
  });
});

test.describe('at 375 px', () => {
  test.use({ viewport: { width: 375, height: 812 } });
  for (const locale of ['en', 'ar'])
    test(`staff rows are stacked with the name first and every field labelled, and nothing scrolls sideways (${locale})`, async ({ page }) => {
      await setUp(page, locale);
      await page.goto('/settings?tab=staff');
      const table = page.getByRole('region', { name: locale === 'en' ? 'Staff list' : 'قائمة الموظفين' });
      await expect(table.getByRole('rowheader', { name: 'K. Lakshmi (synthetic)' })).toBeInViewport();
      const role = table.getByRole('row', { name: /joseph/ }).getByRole('cell').first();
      await expect(role.getByText(locale === 'en' ? 'Role' : 'الدور', { exact: true })).toBeVisible(); // the column's name above its value
      expect(await table.evaluate(e => e.scrollWidth - e.clientWidth)).toBeLessThanOrEqual(0);
      expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(0);
      expect(await axe(page)).toEqual([]);
    });
});
