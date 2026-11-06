import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P2-024: the owner's access-request queue and the clinic policy in Settings -> Staff, and the bell's pointer to them. CI has no API:
// the endpoints are mocked in the shapes the server sends; who may decide, the expiry, the own-request rule and the audit rows are covered by
// ClinicalAccessTests and AccessRequestEndpointTests on PostgreSQL.
const facility = '11111111-1111-4111-8111-111111111111';
const admin = {
  user: { id: 'u-lakshmi', displayName: 'K. Lakshmi (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: facility, facilityName: 'Adyar', role: 'PracticeAdmin' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: facility, role: 'PracticeAdmin', flags: [] as string[], capabilities: { StaffRolesIntegrations: 'Config', RegisterBookPatients: 'Full' } },
};
const pending = () => [
  { id: 'r-1', patientId: 'p-1', patientName: 'Meera Iyer (synthetic)', mrn: '000001', requestedBy: 'u-menon', requesterName: 'Dr. S. Menon (synthetic)', reason: 'CoveringColleague', requestedAt: '2026-11-06T04:00:00Z' },
  { id: 'r-2', patientId: 'p-2', patientName: 'Deepak Nair (synthetic)', mrn: '000002', requestedBy: 'u-nurse', requesterName: 'R. Priya (synthetic)', reason: 'SeeingPatientToday', requestedAt: '2026-11-06T05:00:00Z' },
];

async function open(page: Page, { locale = 'en', path = '/settings?tab=staff', refuse = '' } = {}) {
  const state = { requests: pending(), policy: 'AllFacilityClinicians', decisions: [] as { url: string; body: string; csrf: string | undefined }[], saved: [] as string[] };
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: admin }));
  await page.route('**/v1/settings/locations', r => r.fulfill({ json: [{ id: facility, name: 'Adyar (synthetic)', address: null, timeZone: 'Asia/Kolkata' }] }));
  await page.route('**/v1/staff', r => r.fulfill({ json: [] }));
  await page.route('**/v1/staff/roles', r => r.fulfill({ json: { builtIn: [], custom: [] } }));
  await page.route('**/v1/staff/access-review', r => r.fulfill({ json: { quarter: '2026-Q4', completedAt: null, lastCompletedAt: null, members: [] } }));
  await page.route('**/v1/access-requests', r => r.fulfill({ json: { requests: state.requests, grantDays: [1, 7, 30] } }));
  await page.route('**/v1/access-requests/*/*', r => {
    const [id, action] = r.request().url().split('/').slice(-2);
    if (refuse) return r.fulfill({ status: 409, json: { code: refuse } });
    state.decisions.push({ url: `${id}/${action}`, body: r.request().postData() ?? '', csrf: r.request().headers()['x-medvya-csrf'] });
    state.requests = state.requests.filter(x => x.id !== id);
    return r.fulfill({ json: { id, status: action === 'grant' ? 'Granted' : 'Declined' } });
  });
  await page.route('**/v1/settings/clinical-access', r => {
    if (r.request().method() === 'PUT') {
      state.policy = (JSON.parse(r.request().postData() ?? '{}') as { policy: string }).policy;
      state.saved.push(state.policy);
    }
    return r.fulfill({ json: { policy: state.policy } });
  });
  await page.route('**/v1/notifications/inbox?*', r => r.fulfill({ json: { unread: 1, items: [
    { id: 'n-1', kind: 'AccessRequested', refId: 'r-1', detail: null, createdAt: '2026-11-06T04:00:00Z', read: false },
  ] } }));
  await page.goto(path);
  return state;
}

test('the owner grants one request for a week and declines the other', async ({ page }) => {
  const state = await open(page);
  const queue = page.getByRole('region', { name: 'Access requests' });
  await expect(queue).toContainText('Dr. S. Menon (synthetic) asks to open Meera Iyer (synthetic)');
  await expect(queue).toContainText('MRN 000001 · Covering for a colleague');
  await expect(queue.getByRole('combobox', { name: 'How long Dr. S. Menon (synthetic) may open the record' })).toHaveText('1 day'); // shortest by default

  await queue.getByRole('combobox', { name: 'How long Dr. S. Menon (synthetic) may open the record' }).click();
  await page.getByRole('option', { name: '7 days' }).click();
  await queue.getByRole('button', { name: 'Grant access to Dr. S. Menon (synthetic)' }).click();
  await expect(queue).not.toContainText('Meera Iyer');
  await queue.getByRole('button', { name: 'Decline the request from R. Priya (synthetic)' }).click();
  await expect(queue).toContainText('No requests waiting.');
  expect(state.decisions).toEqual([{ url: 'r-1/grant', body: '{"days":7}', csrf: '1' }, { url: 'r-2/decline', body: '{}', csrf: '1' }]);
});

test('an owner cannot decide their own request, and is told why', async ({ page }) => {
  await open(page, { refuse: 'own_request' });
  const queue = page.getByRole('region', { name: 'Access requests' });
  await queue.getByRole('button', { name: 'Grant access to Dr. S. Menon (synthetic)' }).click();
  await expect(queue.getByRole('alert')).toHaveText('You cannot decide your own request. Ask another owner.');
});

test('the owner limits record opening to each patient\'s own clinicians', async ({ page }) => {
  const state = await open(page);
  const policy = page.getByRole('region', { name: 'Who opens patient records' });
  await expect(policy.getByRole('radio', { name: /Every clinician of this clinic/ })).toBeChecked();
  await policy.getByRole('radio', { name: /Only the patient's own clinicians/ }).check();
  await policy.getByRole('button', { name: 'Save changes' }).click();
  await expect(policy.getByRole('status')).toHaveText('Saved.');
  expect(state.saved).toEqual(['AssignedOnly']);
});

test('the bell points the owner at the request', async ({ page }) => {
  await open(page, { path: '/schedule' });
  await page.getByRole('button', { name: 'Notifications, 1 unread' }).click();
  const panel = page.getByRole('dialog', { name: 'Notifications' });
  await expect(panel).toContainText("A clinician asked for access to a patient's record");
  await panel.getByRole('link').first().click();
  await expect(page).toHaveURL(/\/settings\?tab=staff$/);
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const locale of ['en', 'ar'])
  test(`axe finds no violations on access requests and the policy (${locale}); 44 px targets`, async ({ page }) => {
    await open(page, { locale });
    const queue = page.getByRole('region', { name: locale === 'ar' ? 'طلبات الوصول' : 'Access requests' });
    await expect(queue.getByRole('listitem')).toHaveCount(2);
    if (locale === 'ar') {
      await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
      await expect(queue.getByRole('combobox').first()).toHaveText('يوم واحد');
    }
    for (const b of await queue.getByRole('button').all()) expect((await b.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
        .map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
  });
