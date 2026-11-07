import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P1-005 / MDV-P1-010: the clinic profile keeps the registration's expiry date, which drives the owners' reminders. CI has no API,
// so the endpoints are mocked in the shapes the server sends (ProfileView).
const owner = {
  user: { id: 'u-o', displayName: 'K. Lakshmi (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role: 'PracticeAdmin' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'PracticeAdmin', flags: [] as string[], capabilities: { StaffRolesIntegrations: 'Full' } },
};
const profile = { name: 'Iyer Family Clinic (synthetic)', address: '12 Test Street, Chennai', registrationNumber: 'CE-0001', registrationExpiresOn: null, taxNumber: null, phone: null, email: 'desk@clinic.example', hasLogo: false };

async function open(page: Page, locale = 'en') {
  const saved: unknown[] = [];
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: owner }));
  await page.route('**/v1/notifications/inbox?*', r => r.fulfill({ json: { unread: 0, items: [] } }));
  await page.route('**/v1/settings/profile', r => {
    if (r.request().method() === 'PUT') { const body = r.request().postDataJSON() as Record<string, unknown>; saved.push(body); return r.fulfill({ json: { ...profile, ...body, hasLogo: false } }); }
    return r.fulfill({ json: profile });
  });
  await page.goto('/settings?tab=profile');
  return saved;
}

test('the registration expiry date is saved with the profile', async ({ page }) => {
  const saved = await open(page);
  const date = page.getByLabel('Registration valid until');
  await expect(date).toBeVisible();
  await date.fill('2027-03-31');
  await page.getByRole('button', { name: 'Save' }).click();
  await expect.poll(() => saved.length).toBe(1);
  expect(saved[0]).toMatchObject({ registrationNumber: 'CE-0001', registrationExpiresOn: '2027-03-31' });
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const locale of ['en', 'ar'])
  test(`axe finds no violations on the profile tab (${locale})`, async ({ page }) => {
    await open(page, locale);
    await expect(page.locator('#profile-registration-expires')).toBeVisible();
    if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations.map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
  });
