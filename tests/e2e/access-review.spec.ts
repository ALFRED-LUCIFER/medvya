import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P1-010: the quarterly access review in Settings -> Staff, owner reminders in the notification centre, and an expired licence on the
// Doctors tab. CI has no API: the endpoints are mocked in the shapes the server sends; the server side (completion, audit, expiry gate,
// 60/30/7/0 reminders) is covered by NotificationsTests.Compliance and DoctorTests on SQL Server.
const facility = '11111111-1111-4111-8111-111111111111';
const admin = {
  user: { id: 'u-lakshmi', displayName: 'K. Lakshmi (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: facility, facilityName: 'Adyar', role: 'PracticeAdmin' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: facility, role: 'PracticeAdmin', flags: [] as string[], capabilities: { StaffRolesIntegrations: 'Config', RegisterBookPatients: 'Full' } },
};
const members = () => [
  { membershipId: 'a', name: 'K. Lakshmi (synthetic)', role: 'PracticeAdmin', facilityName: 'Adyar (synthetic)', lastSignIn: '2026-11-05T05:00:00Z', decision: null as string | null },
  { membershipId: 'b', name: 'A. Joseph (synthetic)', role: 'Receptionist', facilityName: 'Adyar (synthetic)', lastSignIn: null, decision: null as string | null },
];

async function open(page: Page, { locale = 'en', path = '/settings?tab=review' } = {}) {
  const state = { members: members(), completedAt: null as string | null, confirms: [] as { id: string; csrf: string | undefined }[] };
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: admin }));
  await page.route('**/v1/settings/locations', r => r.fulfill({ json: [{ id: facility, name: 'Adyar (synthetic)', address: null, timeZone: 'Asia/Kolkata' }] }));
  await page.route('**/v1/staff', r => r.fulfill({ json: [] }));
  await page.route('**/v1/staff/roles', r => r.fulfill({ json: { builtIn: [], custom: [] } }));
  await page.route('**/v1/staff/access-review', r => r.fulfill({ json: { quarter: '2026-Q4', completedAt: state.completedAt, lastCompletedAt: state.completedAt, members: state.members } }));
  await page.route('**/v1/staff/access-review/*/confirm', r => {
    const id = r.request().url().split('/').at(-2)!;
    state.confirms.push({ id, csrf: r.request().headers()['x-medvya-csrf'] });
    state.members.find(m => m.membershipId === id)!.decision = 'Confirmed';
    if (state.members.every(m => m.decision)) state.completedAt = '2026-11-06T04:00:00Z';
    return r.fulfill({ status: 204 });
  });
  await page.route('**/v1/notifications/inbox?*', r => r.fulfill({ json: { unread: 2, items: [
    { id: 'n-1', kind: 'LicenceExpiring', refId: 'd-1', detail: 7, createdAt: '2026-11-05T04:00:00Z', read: false },
    { id: 'n-2', kind: 'AccessReviewDue', refId: null, detail: null, createdAt: '2026-11-05T03:00:00Z', read: false },
  ] } }));
  await page.goto(path);
  return state;
}

test('the owner confirms each person; the last confirmation shows the completion date', async ({ page }) => {
  const state = await open(page);
  const review = page.getByRole('region', { name: 'Quarterly access review, 2026-Q4' });
  await expect(review).toContainText('2 to review');
  await expect(review).toContainText('No review has been completed yet.');
  await expect(review.getByRole('listitem').first()).toContainText('Practice admin');
  await expect(review.getByRole('listitem').nth(1)).toContainText('has not signed in yet');
  await review.getByRole('button', { name: 'Confirm access for K. Lakshmi (synthetic)' }).click();
  await expect(review).toContainText('1 to review');
  await review.getByRole('button', { name: 'Confirm access for A. Joseph (synthetic)' }).click();
  await expect(review).toContainText('Review completed on');
  await expect(review).toContainText('Last completed review:');
  expect(state.confirms).toEqual([{ id: 'a', csrf: '1' }, { id: 'b', csrf: '1' }]);
});

test('the bell lists the licence and review reminders and links to where they are handled', async ({ page }) => {
  await open(page, { path: '/schedule' });
  await page.getByRole('button', { name: 'Notifications, 2 unread' }).click();
  const panel = page.getByRole('dialog', { name: 'Notifications' });
  await expect(panel).toContainText("A doctor's registration (licence) expires in 7 days or less");
  await expect(panel).toContainText('The quarterly access review is due');
  await panel.getByRole('link').nth(1).click();
  await expect(page).toHaveURL(/\/settings\?tab=review$/);
});

test('an expired licence shows as Expired with Renew licence', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('medvya.locale', 'en'));
  await page.route('**/v1/me/context', r => r.fulfill({ json: admin }));
  await page.route('**/v1/settings/locations', r => r.fulfill({ json: [{ id: facility, name: 'Adyar (synthetic)', address: null, timeZone: 'Asia/Kolkata' }] }));
  await page.route('**/v1/settings/doctors', r => r.fulfill({ json: {
    doctors: [{ id: 'd-1', fullName: 'Dr. Arjun Rao (synthetic)', specialty: null, qualifications: null, languages: null, facilityId: facility, facilityName: 'Adyar (synthetic)',
      status: 'Active', authority: 'TNMC', number: '84213', category: 'Permanent', expiresOn: '2026-11-01', expired: true, attestedBy: 'u-lakshmi', attestedByName: null, attestedAt: '2026-11-01T09:00:00Z', hasSignature: false }],
    defaults: { newVisitFee: 50000, followUpFee: 30000, currency: 'INR' }, authorities: [{ code: 'TNMC', name: 'Tamil Nadu Medical Council' }] } }));
  await page.route('**/v1/notifications/inbox?*', r => r.fulfill({ json: { unread: 0, items: [] } }));
  await page.goto('/settings?tab=doctors');
  await expect(page.getByText('Licence expired', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Renew licence' })).toBeVisible();
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const locale of ['en', 'ar'])
  test(`axe finds no violations on the access review (${locale}); 44 px targets`, async ({ page }) => {
    await open(page, { locale });
    await expect(page.getByRole('region', { name: /2026-Q4/ })).toBeVisible();
    if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    for (const b of await page.getByRole('region', { name: /2026-Q4/ }).getByRole('button').all()) expect((await b.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
        .map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
  });
