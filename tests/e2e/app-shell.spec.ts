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
  // A clinical address is not a route for this role: the page says so (MDV-P1-096), not "Page not found".
  await page.goto('/patients');
  await expect(page.getByRole('heading', { name: 'Screen not available' })).toBeVisible();
});

test('doctor sees Clinical and Practice items; flag-off items are not rendered', async ({ page }) => {
  await signInAs(page, doctor);
  await page.goto('/');
  await expect(page).toHaveURL('/today');
  const nav = page.getByRole('navigation');
  await expect(nav.getByRole('heading')).toHaveText(['Clinical', 'Practice', 'Admin']);
  await expect(nav.getByRole('link')).toHaveText(['Today', 'Patients', 'Front desk', 'Schedule', 'Register patient', 'Practice settings']);
  await expect(nav.getByRole('link', { name: 'Today' })).toHaveAttribute('aria-current', 'page');
  // MDV-P2-002: a doctor's Today is their own screen, not the reception board.
  await expect(page.getByRole('main').getByRole('heading', { name: /^Today/ })).toBeVisible();
});

test('a member of one clinic who has not chosen it yet goes straight in: there is nothing to pick (UX-08)', async ({ page }) => {
  // (A user with no clinic at all is sent to the setup wizard, MDV-P1-004; two or more see the picker, session-control.spec.ts.)
  let chosen = false;
  const posts: unknown[] = [];
  await page.addInitScript(() => localStorage.setItem('medvya.locale', 'en'));
  await page.route('**/v1/me/context', r => r.fulfill({ json: chosen ? doctor : { user: doctor.user, memberships: doctor.memberships, active: null } }));
  await page.route('**/v1/me/facility', r => { posts.push(r.request().postDataJSON()); chosen = true; return r.fulfill({ status: 204 }); });
  await page.goto('/today');
  await expect(page.getByRole('main').getByRole('heading', { name: /^Today/ })).toBeVisible();
  await expect(page).toHaveURL('/today'); // back to the address asked for
  await expect(page.getByRole('heading', { name: 'Where are you working today?' })).toHaveCount(0);
  expect(posts).toEqual([{ membershipId: 'm-1' }]);
});

const nurse = {
  user: { id: 'u-anu', displayName: 'Sister Anu' },
  memberships: [membership('Nurse')],
  active: {
    membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'Nurse', flags: ['clinical.nurse-intake'],
    capabilities: { RegisterBookPatients: 'Read', CheckInQueue: 'Full', Patient360Chart: 'Limited', VitalsAllergiesEntry: 'Full', ClinicalNotes: 'Read', CareTasksPatientContact: 'Full' },
  },
};

test('a nurse lands on the nurse queue, and /patients/new says the role cannot register instead of "patient not found" (UX-08)', async ({ page }) => {
  await signInAs(page, nurse);
  await page.route('**/v1/front-desk/board?*', r => r.fulfill({ json: { facilityId: 'f-1', timeZone: 'Asia/Kolkata', date: '2026-10-19', now: '2026-10-19T04:40:00Z', cards: [] } }));
  await page.goto('/');
  await expect(page).toHaveURL('/nurse');
  await page.goto('/patients/new');
  await expect(page.getByRole('heading', { name: 'Register patient' })).toBeVisible();
  await expect(page.getByText('Your role at this facility cannot register patients.')).toBeVisible();
  await expect(page.getByText('This patient was not found')).toHaveCount(0);
});

test('page not found offers the start page, not Today, to a role without Today (UX-08)', async ({ page }) => {
  await signInAs(page, receptionist);
  await page.goto('/no-such-page');
  await expect(page.getByRole('heading', { name: 'Page not found' })).toBeVisible();
  await page.getByRole('button', { name: 'Go to my start page' }).click();
  await expect(page).toHaveURL('/front-desk');
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
    await expect(page.getByRole('main').getByRole('link')).toHaveCount(7); // includes My devices (MDV-P0-012)
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
      await expect(page.getByRole('main').getByRole('heading', { level: 1 })).toBeVisible();
      await page.addScriptTag({ path: axePath });
      const violations = await page.evaluate(async () =>
        (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
          .map(v => `${v.id} (${v.nodes.length})`));
      expect(violations).toEqual([]);
    });

// MDV-P1-096: a screen the role may not open says so, with the start page; an address that does not exist stays "Page not found".
for (const locale of ['en', 'ar'])
  test(`a refused screen says the role does not include it, offers the start page, names nothing, and passes axe (${locale})`, async ({ page }) => {
    await signInAs(page, receptionist, locale);
    const [title, body, home] = locale === 'en'
      ? ['Screen not available', 'Your role does not include this screen.', 'Go to my start page']
      : ['الشاشة غير متاحة', 'دورك لا يشمل هذه الشاشة.', 'الانتقال إلى صفحتي الرئيسية'];
    for (const path of ['/today', '/settings', '/consult/e-7', '/consult/e-7/medicines', '/patients/p-1']) {
      await page.goto(path);
      await expect(page.getByRole('main').getByRole('heading', { level: 1 })).toHaveText(title);
      await expect(page.getByRole('heading', { level: 1 })).toHaveCount(1);
      await expect(page.getByRole('main')).toContainText(body);
      await expect(page.getByText(/not found|غير موجودة/i)).toHaveCount(0);
      // Only the address typed and the fixed words: no patient, MRN or clinical word is shown or added to the address.
      expect(new URL(page.url()).pathname).toBe(path);
      expect(await page.getByRole('main').textContent()).not.toMatch(/Meera|MRN|allerg|prescri|diagnos/i);
    }
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
        .map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
    await page.getByRole('button', { name: home }).click();
    await expect(page).toHaveURL('/front-desk');
  });

test('a nurse opening a consultation sees the one refused message and no red error lines', async ({ page }) => {
  await signInAs(page, nurse);
  let asked = 0;
  await page.route('**/v1/encounters/**', r => { asked++; return r.fulfill({ status: 403 }); });
  await page.goto('/consult/e-7');
  await expect(page.getByRole('heading', { name: 'Screen not available' })).toBeVisible();
  await expect(page.getByRole('main').getByRole('alert')).toHaveCount(0);
  expect(asked).toBe(0); // the screen is not opened, so nothing is fetched to fail
});

test('an address that does not exist, or a switched-off screen, still says Page not found', async ({ page }) => {
  await signInAs(page, doctor);
  for (const path of ['/no-such-page', '/messages', '/consult/e-7/nothing']) {
    await page.goto(path);
    await expect(page.getByRole('heading', { name: 'Page not found' })).toBeVisible();
  }
});
