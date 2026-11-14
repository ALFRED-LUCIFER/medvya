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

// MDV-P2-058: a failed save puts focus on the error summary, in view on a phone and a desktop; its link focuses the field. Axe is clean with
// the errors showing, and required is marked in words.
for (const locale of ['en', 'ar'])
  for (const width of [375, 1440])
    test(`a failed save focuses the error summary, whose link focuses the field (${locale}, ${width}px)`, async ({ page }) => {
      await page.setViewportSize({ width, height: 800 });
      const saved = await open(page, locale);
      await expect(page.locator('#profile-name')).toHaveAccessibleName(locale === 'en' ? 'Clinic name (required)' : /\(مطلوب\)$/);
      await page.locator('#profile-name').fill('');
      await page.locator('#profile-email').fill('not an email');
      await page.locator('form button[type=submit]').first().click();
      const summary = page.getByRole('group', { name: locale === 'en' ? 'There is a problem' : 'توجد مشكلة' });
      await expect(summary).toBeFocused();
      await expect(summary).toBeInViewport();
      const links = summary.getByRole('link');
      await expect(links).toHaveCount(2);
      if (locale === 'en') await expect(links.first()).toHaveText(/^Clinic name: /);
      await links.nth(0).click();
      await expect(page.locator('#profile-name')).toBeFocused();
      await links.nth(1).click();
      await expect(page.locator('#profile-email')).toBeFocused();
      expect(saved).toHaveLength(0);
      await page.addScriptTag({ path: axePath });
      const violations = await page.evaluate(async () =>
        (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations.map(v => `${v.id} (${v.nodes.length})`));
      expect(violations).toEqual([]);
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

// MDV-P2-055 AC5: at 1024 px and wider a group's sections are a left sub-navigation (right in Arabic) in place of the second tab row; narrower,
// the row stays. The five groups and every ?tab= address open the same section as before.
const sections: Record<string, string> = {
  profile: 'Profile', locations: 'Locations', hours: 'Hours', holidays: 'Holidays', displays: 'Queue displays', metrics: 'Pilot metrics', terms: 'Terms',
  staff: 'Staff', doctors: 'Doctors', nurse: 'Nurse intake', queue: 'Queue order', medicines: 'Medicines', notifications: 'Notifications',
  requests: 'Access requests', policy: 'Who opens records', review: 'Access review', audit: 'Audit log', data: 'Data',
};
async function settings(page: Page, locale = 'en') {
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: owner }));
  await page.route('**/v1/notifications/inbox?*', r => r.fulfill({ json: { unread: 0, items: [] } }));
  await page.route('**/v1/settings/profile', r => r.fulfill({ json: profile }));
}

test('at 1024 px and wider the sections are a left sub-navigation and every ?tab= address opens its section', async ({ page }) => {
  test.setTimeout(90_000); // 19 full page loads on the dev server: the 30 s default runs out on CI runners
  await page.setViewportSize({ width: 1024, height: 900 });
  await settings(page);
  for (const [id, name] of Object.entries(sections)) {
    await page.goto(`/settings?tab=${id}`);
    if (id === 'notifications') { // the Messages group has one section: no sub-navigation
      await expect(page.getByRole('tab', { name: 'Messages', selected: true })).toBeVisible();
      await expect(page.getByRole('tablist', { name: 'Settings sections' })).toHaveCount(0);
      continue;
    }
    const list = page.getByRole('tablist', { name: 'Settings sections' });
    await expect(list.getByRole('tab', { name, exact: true, selected: true })).toBeVisible();
    await expect(list).toHaveAttribute('aria-orientation', 'vertical');
  }

  await page.goto('/settings?tab=profile');
  const list = page.getByRole('tablist', { name: 'Settings sections' });
  const panel = page.getByRole('tabpanel', { name: 'Profile' });
  await expect(panel).toBeVisible();
  expect((await list.boundingBox())!.x + (await list.boundingBox())!.width).toBeLessThanOrEqual((await panel.boundingBox())!.x); // beside, on the left
  await list.getByRole('tab', { name: 'Profile' }).focus();
  await page.keyboard.press('ArrowDown'); // a vertical list moves with up and down
  await expect(list.getByRole('tab', { name: 'Locations', selected: true })).toBeFocused();
  await expect(page).toHaveURL(/\?tab=locations$/);

  await page.setViewportSize({ width: 800, height: 900 }); // narrower: the second row comes back, live
  await expect(list).toHaveAttribute('aria-orientation', 'horizontal');
  const row = (await list.boundingBox())!;
  expect(row.y + row.height).toBeLessThanOrEqual((await page.getByRole('tabpanel', { name: 'Locations' }).boundingBox())!.y); // above the panel
});

for (const locale of ['en', 'ar'])
  test(`the settings sub-navigation is axe clean and on the reading side (${locale})`, async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 900 });
    await settings(page, locale);
    await page.goto('/settings?tab=profile');
    await expect(page.locator('#profile-registration-expires')).toBeVisible();
    await expect(page.locator('html')).toHaveAttribute('dir', locale === 'ar' ? 'rtl' : 'ltr');
    const list = (await page.getByRole('tablist', { name: locale === 'ar' ? 'أقسام الإعدادات' : 'Settings sections' }).boundingBox())!;
    const panel = (await page.getByRole('tabpanel', { name: locale === 'ar' ? 'الملف التعريفي' : 'Profile' }).boundingBox())!;
    if (locale === 'ar') expect(list.x).toBeGreaterThanOrEqual(panel.x + panel.width);
    else expect(list.x + list.width).toBeLessThanOrEqual(panel.x);
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations.map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
  });
