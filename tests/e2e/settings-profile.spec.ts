import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P1-005 / MDV-P1-010: the clinic profile keeps the registration's expiry date, which drives the owners' reminders. CI has no API,
// so the endpoints are mocked in the shapes the server sends (ProfileView).
const owner = {
  user: { id: 'u-o', displayName: 'K. Lakshmi (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role: 'PracticeAdmin' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'PracticeAdmin', flags: [] as string[], capabilities: { StaffRolesIntegrations: 'Full' } },
};
const profile = { name: 'Iyer Family Clinic (synthetic)', address: '12 Test Street, Chennai', registrationNumber: 'CE-0001', registrationExpiresOn: null, taxNumber: null, phone: null, email: 'desk@clinic.example', hasLogo: false, version: 3 };

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

// #889: another administrator saved the profile first. The save is refused (409 "changed"), the form says so, and Reload shows what they saved.
for (const locale of ['en', 'ar'])
  test(`saving a profile someone else changed says so and reloads (${locale})`, async ({ page }) => {
    const saved = await open(page, locale);
    await expect(page.locator('#profile-name')).toHaveValue(profile.name);
    const other = { ...profile, phone: '+91 90000 00001', version: 4 }; // what the other administrator saved
    let reloaded = false;
    await page.route('**/v1/settings/profile', r => {
      if (r.request().method() !== 'PUT') return r.fulfill({ json: reloaded ? other : profile });
      saved.push(r.request().postDataJSON());
      return r.fulfill({ status: 409, json: { code: 'changed' } });
    });
    await page.locator('#profile-email').fill('front@clinic.example');
    await page.locator('form button[type=submit]').first().click();
    const alert = page.getByRole('alert').filter({ has: page.getByRole('button') });
    await expect(alert).toBeVisible();
    expect(saved.at(-1)).toMatchObject({ version: 3 });
    if (locale === 'en') await expect(alert).toContainText('These details were changed by someone else. Nothing was saved.');
    else await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations.map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);

    reloaded = true;
    await alert.getByRole('button').click();
    await expect(page.locator('#profile-phone')).toHaveValue('+91 90000 00001');
    await expect(page.locator('#profile-email')).toHaveValue('desk@clinic.example'); // my unsaved edit is gone
    await expect(alert).toHaveCount(0);
  });

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
