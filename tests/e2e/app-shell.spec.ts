import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// CI has no API: /v1/me/context is mocked with synthetic personas (docs/00-overview/personas.md) in the agreed MDV-P0-012 shape.
const membership = (role: string) => ({
  id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role,
});
const receptionist = {
  user: { id: 'u-joseph', displayName: 'A. Joseph' },
  memberships: [membership('Receptionist')],
  active: {
    membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'Receptionist', flags: [],
    capabilities: { RegisterBookPatients: 'Full', CheckInQueue: 'Full', InvoicesPayments: 'Full', Patient360Chart: 'None', ClinicalNotes: 'None' },
  },
};
const doctor = {
  user: { id: 'u-rao', displayName: 'Dr. Arjun Rao' },
  memberships: [membership('SoloDoctor')],
  active: {
    membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'SoloDoctor', flags: ['clinical.today'],
    capabilities: {
      RegisterBookPatients: 'Full', CheckInQueue: 'Full', InvoicesPayments: 'Full', Patient360Chart: 'Full', VitalsAllergiesEntry: 'Full',
      ClinicalNotes: 'Sign', ResultReviewAcknowledge: 'Sign', CareTasksPatientContact: 'Full', DevicePairingFleet: 'Own', StaffRolesIntegrations: 'Config',
    },
  },
};

async function signInAs(page: Page, context: object, locale = 'en') {
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: context }));
}

const sidebar = (page: Page) => page.locator('nav:visible').first();

test('receptionist sees only Practice items and lands on the front desk', async ({ page }) => {
  await signInAs(page, receptionist);
  await page.goto('/');
  await expect(page).toHaveURL('/front-desk');
  const nav = page.getByRole('navigation');
  await expect(nav.getByRole('heading')).toHaveText(['Practice']);
  await expect(nav.getByRole('link')).toHaveText(['Front desk', 'Schedule', 'Register patient']);
  await expect(nav).toContainText('A. Joseph');
  await expect(nav).toContainText('Receptionist');
  // A clinical address is not a route for this role.
  await page.goto('/patients');
  await expect(page.getByRole('heading', { name: 'Page not found' })).toBeVisible();
});

test('doctor sees Clinical and Practice items; flag-off items are not rendered', async ({ page }) => {
  await signInAs(page, doctor);
  await page.goto('/');
  await expect(page).toHaveURL('/today');
  const nav = page.getByRole('navigation');
  await expect(nav.getByRole('heading')).toHaveText(['Clinical', 'Practice', 'Admin']);
  await expect(nav.getByRole('link')).toHaveText(['Today', 'Patients', 'Front desk', 'Schedule', 'Register patient', 'Practice settings']);
  await expect(nav.getByRole('link', { name: 'Today' })).toHaveAttribute('aria-current', 'page');
  await expect(page.getByRole('main').getByRole('heading', { name: 'Today' })).toBeVisible();
});

test('signed in without an active facility goes to the facility slot', async ({ page }) => {
  await signInAs(page, { user: { id: 'u-rao', displayName: 'Dr. Arjun Rao' } }); // today's server: user only
  await page.goto('/today');
  await expect(page).toHaveURL('/choose-facility');
  await expect(page.getByRole('heading', { name: 'Choose where you are working today' })).toBeVisible();
});

for (const locale of ['en', 'ar']) {
  test(`layout switches at 768 and 1280 px (${locale})`, async ({ page }) => {
    await signInAs(page, doctor, locale);
    const rtl = locale === 'ar';

    await page.setViewportSize({ width: 1366, height: 800 });
    await page.goto('/today');
    let box = (await sidebar(page).boundingBox())!;
    expect(box.width).toBeGreaterThanOrEqual(196);
    expect(box.width).toBeLessThanOrEqual(232);
    expect(rtl ? box.x + box.width : box.x).toBe(rtl ? 1366 : 0); // sidebar sits on the reading-start side
    await expect(sidebar(page)).toContainText('Iyer Family Clinic · Adyar');

    await page.setViewportSize({ width: 900, height: 800 });
    box = (await sidebar(page).boundingBox())!;
    expect(box.width).toBe(72);
    await expect(page.locator('nav:visible')).toHaveCount(1);

    await page.setViewportSize({ width: 375, height: 760 });
    const tabs = page.locator('nav:visible');
    await expect(tabs).toHaveCount(1);
    box = (await tabs.boundingBox())!;
    expect(box.y + box.height).toBe(760);
    await expect(tabs.getByRole('link')).toHaveCount(5); // Today · Patients · Front desk · Schedule · More (Inbox and Messages are flag-off)
    for (const link of await tabs.getByRole('link').all()) expect((await link.boundingBox())!.height).toBeGreaterThanOrEqual(48);
    await tabs.getByRole('link').last().click();
    await expect(page).toHaveURL('/more');
    await expect(page.getByRole('main').getByRole('link')).toHaveCount(6);
  });
}

test('keyboard: skip link is first, visible on focus and moves focus to main', async ({ page }) => {
  await signInAs(page, receptionist);
  await page.goto('/front-desk');
  // goto resolves on load, but the shell renders only after /v1/me/context answers; a Tab during loading lands nowhere.
  await expect(page.getByRole('main').getByRole('heading', { name: /^Reception board/ })).toBeVisible(); // the P-01 board (MDV-P1-028)
  await page.keyboard.press('Tab');
  const skip = page.getByRole('link', { name: 'Skip to main content' });
  await expect(skip).toBeFocused();
  expect((await skip.boundingBox())!.width).toBeGreaterThan(1);
  await page.keyboard.press('Tab');
  await expect(page.getByRole('link', { name: 'Front desk' })).toBeFocused(); // then the sidebar, in reading order
  await page.keyboard.press('Shift+Tab');
  await page.keyboard.press('Enter');
  await expect(page.locator('main')).toBeFocused();
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const [name, width] of [['desktop', 1366], ['tablet', 900], ['mobile', 375]] as const)
  for (const locale of ['en', 'ar'])
    test(`axe finds no violations on the shell (${name}, ${locale})`, async ({ page }) => {
      await signInAs(page, doctor, locale);
      await page.setViewportSize({ width, height: 800 });
      await page.goto('/today');
      await expect(page.getByRole('main').getByRole('heading')).toBeVisible();
      await page.addScriptTag({ path: axePath });
      const violations = await page.evaluate(async () =>
        (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
          .map(v => `${v.id} (${v.nodes.length})`));
      expect(violations).toEqual([]);
    });
