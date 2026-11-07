import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P1-012 leave and schedule exceptions. CI has no API: the session, schedule and exception endpoints are mocked in the
// server's shapes with the synthetic cast. The real rules (action per visit, needs rebooking, message, audit, slots) are covered
// by ScheduleAdjustmentTests on SQL Server.
// Leave is managed by the practice admin (every calendar) or the doctor (own calendar), owner decision #231; the desk only views it.
const admin = {
  user: { id: 'u-lakshmi', displayName: 'K. Lakshmi' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role: 'PracticeAdmin' }],
  active: {
    membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'PracticeAdmin', flags: [], country: 'IN',
    capabilities: { RegisterBookPatients: 'Full', CheckInQueue: 'Full', InvoicesPayments: 'Full', Patient360Chart: 'None', ClinicalNotes: 'None', StaffRolesIntegrations: 'Config' },
  },
};
const reception = {
  user: { id: 'u-joseph', displayName: 'A. Joseph' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role: 'Receptionist' }],
  active: {
    membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'Receptionist', flags: [], country: 'IN',
    capabilities: { RegisterBookPatients: 'Full', CheckInQueue: 'Full', InvoicesPayments: 'Full', Patient360Chart: 'None', ClinicalNotes: 'None' },
  },
};
const visit = (id: string, name: string, start: string) => ({
  id, reference: `APT-${id}`, patientId: `p-${id}`, patientName: name, visitType: 'FollowUp', start, end: start, state: 'Booked',
});
const affected = [visit('a1', 'Meera Iyer (synthetic)', '2026-10-19T04:30:00Z'), visit('a2', 'Deepak Nair (synthetic)', '2026-10-19T05:10:00Z')];

async function open(page: Page, locale = 'en', as: typeof admin | typeof reception = admin) {
  const bodies: { actions: { appointmentId: string; action: string }[] }[] = [];
  let saved: unknown[] = [];
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: as }));
  await page.route('**/v1/schedule?*', r => r.fulfill({
    json: { facilityId: 'f-1', timeZone: 'Asia/Kolkata', date: '2026-10-19', start: '2026-10-19T03:30:00Z', end: '2026-10-19T15:30:00Z',
      columns: [{ calendarId: '5e1d0001-0001-4000-8000-000000000001', calendarKind: 'Doctor', name: 'Dr. Arjun Rao (synthetic)', slotMinutes: 20, hours: [], slots: [], unavailable: [], appointments: [], visitLengths: [], capacity: 0 }] },
  }));
  await page.route('**/v1/schedule/exceptions**', async r => {
    if (r.request().method() === 'GET') return r.fulfill({ json: saved });
    const body = r.request().postDataJSON() as { actions: { appointmentId: string; action: string }[] };
    if (body.actions.length < affected.length)
      return r.fulfill({ status: 409, json: { code: 'actions_required', title: 'Affected appointments need an action', affected } });
    bodies.push(body);
    saved = [{ id: 'x1', calendarId: '5e1d0001-0001-4000-8000-000000000001', kind: 'Leave', fromDate: '2026-10-19', toDate: '2026-10-21', start: null, end: null, reason: 'Conference', canEdit: true, version: 'v1' }];
    return r.fulfill({ json: { exception: saved[0], offered: 1, manual: 1 } });
  });
  return bodies;
}

test('leave over booked visits lists them and saves only after each has an action', async ({ page }) => {
  const bodies = await open(page);
  await page.goto('/schedule');
  await page.getByRole('link', { name: 'Leave and exceptions' }).click();
  await page.getByRole('combobox', { name: 'Doctor or room' }).click();
  await page.getByRole('option', { name: 'Dr. Arjun Rao (synthetic)' }).click();
  await page.getByLabel('To', { exact: true }).fill('2026-10-21');
  await page.getByLabel('Reason (optional)').fill('Conference');
  await page.getByRole('button', { name: 'Save' }).click();

  await expect(page.getByRole('heading', { name: 'Booked visits affected: 2' })).toBeVisible();
  const save = page.getByRole('button', { name: 'Save' });
  await expect(save).toBeDisabled();
  await page.getByRole('group', { name: /Meera Iyer/ }).getByLabel(/Offer a new slot/).check();
  await expect(save).toBeDisabled(); // one visit still has no action
  await page.getByRole('group', { name: /Deepak Nair/ }).getByLabel(/Reschedule manually/).check();
  await save.click();

  await expect(page.getByRole('status')).toContainText('Saved.');
  await expect(page.getByRole('status')).toContainText('1 patients will be sent a rebooking message');
  await expect(page.getByText('Conference').last()).toBeVisible();
  expect(bodies[0]!.actions).toEqual([{ appointmentId: 'a1', action: 'Offer' }, { appointmentId: 'a2', action: 'Manual' }]);
});

test('an extra session asks for one date and a window', async ({ page }) => {
  await open(page);
  await page.goto('/schedule/leave');
  await page.getByRole('combobox', { name: 'Type' }).click();
  await page.getByRole('option', { name: 'Extra session' }).click();
  await expect(page.getByLabel('To', { exact: true })).toHaveCount(0);
  await page.getByRole('combobox', { name: 'Doctor or room' }).click();
  await page.getByRole('option', { name: 'Dr. Arjun Rao (synthetic)' }).click();
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByText('Enter a start time before the end time.')).toBeVisible();
});

// Edit and delete a saved exception: the edit is a PUT with the same body as an add; delete asks first and says what stays.
test('a saved leave is edited and deleted, with the note that moved visits stay for the desk', async ({ page }) => {
  await open(page);
  const row = { id: 'x1', calendarId: '5e1d0001-0001-4000-8000-000000000001', kind: 'Leave', fromDate: '2026-10-19', toDate: '2026-10-21', start: null, end: null, reason: 'Conference', canEdit: true, version: 'v1' };
  let saved: unknown[] = [row];
  const calls: string[] = [];
  await page.route('**/v1/schedule/exceptions/x1**', r => {
    const m = r.request().method();
    const body = m === 'PUT' ? r.request().postDataJSON() as { toDate: string; version: string } : null;
    calls.push(body ? `PUT ${body.toDate} ${body.version}` : `${m} ${new URL(r.request().url()).searchParams.get('version')}`);
    if (m === 'DELETE') { saved = []; return r.fulfill({ status: 204 }); }
    saved = [{ ...row, toDate: '2026-10-20', version: 'v2' }];
    return r.fulfill({ json: { exception: saved[0], offered: 0, manual: 0 } });
  });
  await page.route('**/v1/schedule/exceptions?*', r => r.request().method() === 'GET' ? r.fulfill({ json: saved }) : r.fallback());
  await page.goto('/schedule/leave');

  await expect(page.getByText(/Visits already moved to needs rebooking stay as they are/)).toBeVisible();
  await page.getByRole('button', { name: /^Edit leave or exception/ }).click();
  await expect(page.getByText('Editing a saved exception')).toBeVisible();
  await page.getByLabel('To', { exact: true }).fill('2026-10-20');
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect.poll(() => calls).toEqual(['PUT 2026-10-20 v1']); // the version the editor saw goes back with the edit

  await page.getByRole('button', { name: /^Delete leave or exception/ }).click();
  await expect(page.getByText('Visits already moved to needs rebooking are not booked again.')).toBeVisible();
  await page.getByRole('button', { name: 'Yes, delete' }).click();
  await expect.poll(() => calls).toEqual(['PUT 2026-10-20 v1', 'DELETE v2']);
  await expect(page.getByText('None yet.')).toBeVisible();
});

test('the front desk sees leave as "On leave" without a reason and cannot add, edit or delete it (#231)', async ({ page }) => {
  await open(page, 'en', reception);
  const row = { id: 'x1', calendarId: '5e1d0001-0001-4000-8000-000000000001', kind: 'Leave', fromDate: '2026-10-19', toDate: '2026-10-21', start: null, end: null, reason: null, canEdit: false, version: 'v1' };
  await page.route('**/v1/schedule/exceptions?*', r => r.fulfill({ json: [row] }));
  await page.goto('/schedule/leave');
  await expect(page.getByText(/Leave is added by the practice admin or by the doctor/)).toBeVisible();
  await expect(page.getByRole('button', { name: 'Save', exact: true })).toHaveCount(0);
  await expect(page.getByText(/Dr. Arjun Rao \(synthetic\) · Leave/)).toBeVisible();
  await expect(page.getByRole('button', { name: /^Edit leave or exception/ })).toHaveCount(0);
  await expect(page.getByRole('button', { name: /^Delete leave or exception/ })).toHaveCount(0);
});

test('an edit refused because someone else changed the leave says so and reloads the list (#231)', async ({ page }) => {
  await open(page);
  const row = { id: 'x1', calendarId: '5e1d0001-0001-4000-8000-000000000001', kind: 'Leave', fromDate: '2026-10-19', toDate: '2026-10-21', start: null, end: null, reason: 'Conference', canEdit: true, version: 'v1' };
  let lists = 0;
  await page.route('**/v1/schedule/exceptions?*', r => { lists++; return r.fulfill({ json: [row] }); });
  await page.route('**/v1/schedule/exceptions/x1**', r => r.fulfill({ status: 400, json: { title: 'One or more validation errors occurred.', errors: { id: ['stale'] } } }));
  await page.goto('/schedule/leave');
  await page.getByRole('button', { name: /^Edit leave or exception/ }).click();
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page.getByText(/Someone else changed this while you were working/)).toBeVisible();
  await expect.poll(() => lists).toBeGreaterThan(1);
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const locale of ['en', 'ar'])
  test(`axe finds no violations on a saved leave being edited and on its delete question (${locale})`, async ({ page }) => {
    await open(page, locale);
    await page.route('**/v1/schedule/exceptions?*', r => r.fulfill({
      json: [{ id: 'x1', calendarId: '5e1d0001-0001-4000-8000-000000000001', kind: 'Leave', fromDate: '2026-10-19', toDate: '2026-10-21', start: null, end: null, reason: 'Conference', canEdit: true, version: 'v1' }],
    }));
    await page.goto('/schedule/leave');
    await page.locator('#adj-calendar').waitFor();
    await page.locator('ul button').first().click(); // Edit
    await expect(page.locator('#adj-reason')).toHaveValue('Conference');
    await page.locator('ul button').nth(1).click(); // Delete: the question replaces the buttons
    await expect(page.locator('ul button')).toHaveCount(2); // Yes / Keep it
    expect(await page.evaluate(() => document.documentElement.dir)).toBe(locale === 'ar' ? 'rtl' : 'ltr');
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
        .map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
  });

for (const locale of ['en', 'ar'])
  test(`axe finds no violations on leave and exceptions with affected visits (${locale})`, async ({ page }) => {
    await open(page, locale);
    await page.goto('/schedule/leave');
    await page.locator('#adj-calendar').click();
    await page.getByRole('option').first().click();
    await page.getByRole('button', { name: /^Save$|^حفظ$/ }).click();
    await expect(page.locator('#adj-affected')).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.dir)).toBe(locale === 'ar' ? 'rtl' : 'ltr');
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
        .map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
  });
