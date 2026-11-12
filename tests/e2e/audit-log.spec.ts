import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P1-035. CI has no API: the access-log endpoints are mocked with the synthetic cast (docs/00-overview/personas.md).
// The server rules (403 and Denied audit for other roles, patient filter, paging, watermarked audited export) are tested on SQL Server in AccessLogTests.cs.
const facility = '11111111-1111-4111-8111-111111111111';
const admin = {
  user: { id: 'u-lakshmi', displayName: 'K. Lakshmi (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: facility, facilityName: 'Adyar', role: 'PracticeAdmin' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: facility, role: 'PracticeAdmin', flags: [], capabilities: { StaffRolesIntegrations: 'Config', AuditLog: 'Read' } },
};
const row = (over: object) => ({
  id: 'e', occurredAt: '2026-11-04T05:30:00Z', actorUserId: 'u-rao', actorName: 'Dr. Arjun Rao (synthetic)', role: 'SoloDoctor', location: 'Velachery', action: 'patient.read', resourceType: 'patient',
  patientId: 'p-1', patientMrn: '000001', patientName: null, purpose: 'treatment', outcome: 'Allowed', detail: null, ...over,
});
const logPage = (items: object[], next: string | null = null) => ({ items, next, actions: ['patient.read', 'patient.update'] });
const staff = [{ membershipId: 'a', userId: 'u-rao', name: 'Dr. Arjun Rao (synthetic)', email: null, role: 'SoloDoctor', customRoleId: null, customRoleName: null, facilityId: facility, facilityName: 'Adyar', status: 'Active', mfa: 'enrolled', invitationId: null, invitationState: null, invitationExpiresAt: null }];

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
  await page.route('**/v1/staff', r => r.fulfill({ json: staff }));
}

test('the access log shows who did what, writes the outcome out, pages, and exports with the audited POST', async ({ page }) => {
  await setUp(page);
  const urls: string[] = [];
  await page.route('**/v1/audit/access-log**', r => {
    const url = r.request().url();
    if (r.request().method() === 'POST') return r.fulfill({ body: 'x', contentType: 'text/csv' });
    urls.push(new URL(url).pathname + new URL(url).search);
    return r.fulfill({ json: url.includes('after=c1')
      ? logPage([row({ id: 'e3', action: 'patient.update' })])
      : logPage([row({ id: 'e1' }), row({ id: 'e2', actorName: 'S. Thomas RN (synthetic)', role: 'Nurse', outcome: 'Denied', action: 'patient.read' }),
        row({ id: 'e4', action: 'audit.export', role: null, detail: 'patient=p-1' })], 'c1') });
  });
  const exported = page.waitForRequest(r => r.url().endsWith('/v1/audit/access-log/export') && r.method() === 'POST');
  await page.goto('/settings?tab=audit');
  await expect(page.getByRole('heading', { name: 'Practice access log' })).toBeVisible();
  const denied = page.getByRole('row', { name: /S\. Thomas/ });
  await expect(denied).toContainText('Denied'); // written out, not colour alone
  await expect(denied).toContainText('Velachery'); // the location where the record was read (MDV-P1-072)
  await expect(denied).toContainText('MRN 000001');
  const legacy = page.getByRole('row', { name: /Access log exported/ }); // MDV-P1-052: the action in words, not its code
  await expect(legacy).toContainText('Not recorded'); // a row from before roles were recorded never shows the current role
  await expect(legacy).toContainText('patient=p-1');
  await expect(page.getByRole('cell', { name: /Meera/ })).toHaveCount(0); // no name until a patient is chosen
  await expect(page.getByRole('cell', { name: /audit\.|patient\./ })).toHaveCount(0); // no raw action codes
  expect(await axe(page)).toEqual([]);

  await page.getByRole('button', { name: 'Load more' }).click();
  await expect(page.getByRole('row', { name: /Patient record updated/ })).toBeVisible();
  expect(urls.some(u => u.includes('after=c1'))).toBe(true);

  await page.getByRole('button', { name: 'Export CSV' }).click();
  expect(JSON.parse((await exported).postData() ?? '{}')).toMatchObject({ patientId: null, patientRecords: true });
});

// MDV audit UX-13: the log opens on patient-record events, so settings page views do not push them down; "Any action" shows everything.
test('the access log starts on patient records and widens to any action on request', async ({ page }) => {
  await setUp(page);
  const urls: string[] = [];
  await page.route('**/v1/audit/access-log**', r => { urls.push(new URL(r.request().url()).search); return r.fulfill({ json: logPage([row({})]) }); });
  await page.goto('/settings?tab=audit');
  await expect(page.getByRole('combobox', { name: 'Action' })).toHaveText('Patient records');
  await expect.poll(() => urls.at(-1)).toContain('patientRecords=true');
  await page.getByRole('combobox', { name: 'Action' }).click();
  await page.getByRole('option', { name: 'Any action' }).click();
  await expect.poll(() => urls.at(-1)).not.toContain('patientRecords');
});

test('choosing a patient asks the per-patient route and shows the name only then', async ({ page }) => {
  await setUp(page);
  await page.route('**/v1/patients/search', r => r.fulfill({ json: { matchedOn: 'Name', page: 1, pageSize: 20, total: 1, items: [{ id: 'p-1', initials: 'MI', ageYears: 54, ageEstimated: false, sexAtBirth: 'Female', mrnTail: '0001', status: 'Complete' }] } }));
  const paths: string[] = [];
  await page.route('**/v1/audit/access-log**', r => {
    paths.push(new URL(r.request().url()).pathname);
    return r.fulfill({ json: logPage([row({ patientName: 'Meera Iyer (synthetic)' })]) });
  });
  await page.goto('/settings?tab=audit');
  await page.getByPlaceholder('Name, MRN, phone or ID').fill('Meera');
  await page.getByRole('button', { name: 'Search', exact: true }).click();
  await page.getByRole('button', { name: /M I/ }).click();
  await expect(page.getByText('Showing events on the record of Meera Iyer (synthetic)')).toBeVisible();
  expect(paths.at(-1)).toBe('/v1/audit/access-log/patients/p-1');
  await page.getByRole('button', { name: 'Show all patients' }).click();
  await expect.poll(() => paths.at(-1)).toBe('/v1/audit/access-log');
});

test('a role without the audit log sees the refusal, and Arabic lays the table out right to left', async ({ page }) => {
  await setUp(page, 'ar');
  await page.route('**/v1/audit/access-log**', r => r.fulfill({ json: logPage([row({})]) }));
  await page.goto('/settings?tab=audit');
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await expect(page.getByRole('row', { name: /مسموح/ })).toBeVisible();
  expect(await axe(page)).toEqual([]);

  const other = await page.context().newPage();
  await other.addInitScript(() => localStorage.setItem('medvya.locale', 'en'));
  await other.route('**/v1/me/context', r => r.fulfill({ json: admin }));
  await other.route('**/v1/staff', r => r.fulfill({ json: staff }));
  await other.route('**/v1/audit/access-log**', r => r.fulfill({ status: 403, json: {} }));
  await other.goto('/settings?tab=audit');
  await expect(other.getByRole('alert')).toContainText('Your role does not include the audit log');
});

// Found on Azure dev: the date followed the browser's zone and the time the clinic's, so an event after 18:30 UTC showed the right time on the day before.
test.describe('in a browser on another zone', () => {
  test.use({ timezoneId: 'UTC' });
  test('a row shows the day and the time in the clinic\'s zone', async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem('medvya.locale', 'en'));
    await page.route('**/v1/me/context', r => r.fulfill({ json: { ...admin, active: { ...admin.active, timeZone: 'Asia/Kolkata' } } }));
    await page.route('**/v1/staff', r => r.fulfill({ json: staff }));
    await page.route('**/v1/audit/access-log**', r => r.fulfill({ json: logPage([row({ occurredAt: '2026-11-10T19:06:00Z' })]) }));
    await page.goto('/settings?tab=audit');
    await expect(page.getByRole('region', { name: 'Access log' }).locator('time')).toHaveText('11 Nov 2026 00:36'); // 00:36 on 11 Nov in Chennai
  });
});
