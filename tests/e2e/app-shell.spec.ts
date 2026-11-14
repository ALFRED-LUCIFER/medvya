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

test('receptionist sees Front desk · Schedule · Patients and lands on Front desk; Register patient is a button there (MDV-P2-051)', async ({ page }) => {
  await signInAs(page, receptionist);
  await page.route('**/v1/front-desk/board?*', r => r.fulfill({ json: { facilityId: 'f-1', timeZone: 'Asia/Kolkata', date: '2026-10-19', now: '2026-10-19T04:40:00Z', cards: [] } }));
  await page.goto('/');
  await expect(page).toHaveURL('/front-desk');
  const nav = page.getByRole('navigation');
  await expect(nav.getByRole('heading')).toHaveCount(0); // one list in the role's order (blueprint section 3)
  await expect(nav.getByRole('link')).toHaveText(['Front desk', 'Schedule', 'Patients']);
  await expect(page.getByRole('main').getByRole('heading', { level: 1 })).toHaveText(/^Front desk/);
  await expect(page.getByRole('main').getByRole('link', { name: 'Register patient' })).toBeVisible();
  // Name and role are in the account menu (MDV-P1-093), not the sidebar.
  await page.getByRole('button', { name: 'Signed in as A. Joseph' }).click();
  await expect(page.getByRole('menu')).toContainText('Receptionist');
  // A clinical address is not a route for this role: the page says so (MDV-P1-096), not "Page not found".
  await page.goto('/patients/p-1');
  await expect(page.getByRole('heading', { name: 'Screen not available' })).toBeVisible();
  // My day is the doctor's: no menu item, and its address says so too.
  await page.goto('/today');
  await expect(page.getByRole('heading', { name: 'Screen not available' })).toBeVisible();
});

test('doctor sees My day · Patients · Schedule · Front desk · Settings; flag-off items are not rendered (MDV-P2-051)', async ({ page }) => {
  await signInAs(page, doctor);
  await page.goto('/');
  await expect(page).toHaveURL('/today');
  const nav = page.getByRole('navigation');
  await expect(nav.getByRole('link')).toHaveText(['My day', 'Patients', 'Schedule', 'Front desk', 'Practice settings']);
  await expect(nav.getByRole('link', { name: 'My day' })).toHaveAttribute('aria-current', 'page');
  // MDV-P2-002: the doctor's day is their own screen, never the board; the menu and the title say the same name.
  await expect(page.getByRole('main').getByRole('heading', { level: 1 })).toHaveText('My day');
});

test('a member of one clinic who has not chosen it yet goes straight in: there is nothing to pick (UX-08)', async ({ page }) => {
  // (A user with no clinic at all is sent to the setup wizard, MDV-P1-004; two or more see the picker, session-control.spec.ts.)
  let chosen = false;
  const posts: unknown[] = [];
  await page.addInitScript(() => localStorage.setItem('medvya.locale', 'en'));
  await page.route('**/v1/me/context', r => r.fulfill({ json: chosen ? doctor : { user: doctor.user, memberships: doctor.memberships, active: null } }));
  await page.route('**/v1/me/facility', r => { posts.push(r.request().postDataJSON()); chosen = true; return r.fulfill({ status: 204 }); });
  await page.goto('/today');
  await expect(page.getByRole('main').getByRole('heading', { name: 'My day' })).toBeVisible();
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
  // The menu names the nurse queue as its title does, and Today is gone: it used to open the board a second time (AC2).
  await expect(page.getByRole('navigation').getByRole('link')).toHaveText(['Nurse queue', 'Patients', 'Front desk', 'Schedule']);
  await page.getByRole('navigation').getByRole('link', { name: 'Nurse queue' }).click();
  await expect(page.getByRole('main').getByRole('heading', { level: 1 })).toHaveText('Nurse queue');
});

test('a bookmarked /today still opens the board for a nurse without intake, as it did before My day (MDV-P2-051 AC5)', async ({ page }) => {
  await signInAs(page, { ...nurse, active: { ...nurse.active, flags: [] } });
  await page.route('**/v1/front-desk/board?*', r => r.fulfill({ json: { facilityId: 'f-1', timeZone: 'Asia/Kolkata', date: '2026-10-19', now: '2026-10-19T04:40:00Z', cards: [] } }));
  await page.goto('/today');
  await expect(page).toHaveURL('/front-desk');
  await expect(page.getByRole('main').getByRole('heading', { level: 1 })).toHaveText(/^Front desk/);
  // AC2: with intake off, one item per screen.
  await expect(page.getByRole('navigation').getByRole('link')).toHaveText(['Front desk', 'Schedule', 'Patients']);
  // The old Patients address opens the directory, its page.
  await page.goto('/patients');
  await expect(page).toHaveURL('/patients/directory');
});

test('page not found offers the start page, not My day, to a role without My day (UX-08)', async ({ page }) => {
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
    expect(box.width).toBe(232);
    expect(rtl ? box.x + box.width : box.x).toBe(rtl ? 1366 : 0); // sidebar sits on the reading-start side
    await expect(page.getByRole('banner')).toContainText('Iyer Family Clinic · Adyar');

    await page.setViewportSize({ width: 900, height: 800 });
    box = (await sidebar(page).boundingBox())!;
    expect(box.width).toBe(72);
    await expect(page.locator('nav:visible')).toHaveCount(1);

    await page.setViewportSize({ width: 375, height: 760 });
    const tabs = page.locator('nav:visible');
    await expect(tabs).toHaveCount(1);
    box = (await tabs.boundingBox())!;
    expect(box.y + box.height).toBe(760);
    await expect(tabs.getByRole('link')).toHaveText(rtl ? ['يومي', 'المرضى', 'المواعيد', 'الاستقبال', 'المزيد'] : ['My day', 'Patients', 'Schedule', 'Front desk', 'More']); // the role's first four, then More
    for (const link of await tabs.getByRole('link').all()) expect((await link.boundingBox())!.height).toBeGreaterThanOrEqual(48);
    await tabs.getByRole('link').last().click();
    await expect(page).toHaveURL('/more');
    await expect(page.getByRole('main').getByRole('link')).toHaveCount(6); // the five menu items and My devices (MDV-P0-012)
  });
}

test('keyboard: skip link is first, visible on focus and moves focus to main', async ({ page }) => {
  await signInAs(page, receptionist);
  await page.goto('/front-desk');
  // goto resolves on load, but the shell renders only after /v1/me/context answers; a Tab during loading lands nowhere.
  await expect(page.getByRole('main').getByRole('heading', { name: /^Front desk/ })).toBeVisible(); // the P-01 board (MDV-P1-028)
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
for (const [name, width] of [['wide', 1440], ['desktop', 1366], ['laptop', 1024], ['tablet', 900], ['tablet-768', 768], ['mobile', 375]] as const)
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

// MDV-P1-093: one account menu, a 48 px top bar and a labelled rail.
const practiceAdmin = {
  user: { id: 'u-lakshmi', displayName: 'K. Lakshmi' },
  memberships: [membership('PracticeAdmin')],
  active: {
    membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'PracticeAdmin', flags: [],
    capabilities: { RegisterBookPatients: 'Full', CheckInQueue: 'Full', StaffRolesIntegrations: 'Config' },
  },
};
const roles = { receptionist, doctor, nurse, practiceAdmin } as const;
const startPageReady = (page: Page) => expect(page).not.toHaveURL(/\/$/);

for (const [roleName, context] of Object.entries(roles)) {
  for (const width of [768, 375])
    test(`${roleName} at ${width} px: no sideways page scroll, and Sign out in two actions`, async ({ page }) => {
      await signInAs(page, context);
      await page.setViewportSize({ width, height: 800 });
      await page.goto('/');
      await startPageReady(page);
      await expect(page.getByRole('main')).toBeVisible();
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBe(width);
      if (width >= 768) {
        await page.getByRole('button', { name: /^Signed in as / }).click();
        await expect(page.getByRole('menuitem', { name: 'Sign out' })).toBeVisible();
      } else {
        await page.getByRole('link', { name: 'More' }).click();
        await expect(page.getByRole('main').getByRole('button', { name: 'Sign out' })).toBeVisible();
      }
    });

  test(`${roleName} at 1024 by 768: every rail item shows its name under the icon`, async ({ page }) => {
    await signInAs(page, context);
    await page.setViewportSize({ width: 1024, height: 768 });
    await page.goto('/');
    await startPageReady(page);
    const links = sidebar(page).getByRole('link');
    await expect(links.first()).toBeVisible();
    for (const link of await links.all()) {
      const icon = (await link.locator('svg').boundingBox())!;
      const label = link.locator('span').last();
      await expect(label).toBeVisible();
      expect((await label.textContent())!.trim()).toBe((await link.textContent())!.trim()); // the whole name is the visible label
      const box = (await label.boundingBox())!;
      expect(box.width).toBeGreaterThan(10);
      expect(box.y).toBeGreaterThanOrEqual(icon.y + icon.height - 1); // under the icon, not beside or hidden
    }
  });
}

test('at 1440 px the sidebar shows until the user folds it; the labelled rail is kept on reload', async ({ page }) => {
  await signInAs(page, doctor);
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto('/today');
  await expect(sidebar(page).getByRole('link', { name: 'My day' })).toBeVisible();
  expect((await sidebar(page).boundingBox())!.width).toBe(232);
  await page.getByRole('button', { name: 'Collapse menu' }).click();
  expect((await sidebar(page).boundingBox())!.width).toBe(72);
  await expect(sidebar(page).getByRole('link', { name: 'My day' })).toBeVisible(); // labelled, never icon-only
  await page.reload();
  await expect(page.getByRole('button', { name: 'Expand menu' })).toBeVisible();
  expect((await sidebar(page).boundingBox())!.width).toBe(72);
  await page.getByRole('button', { name: 'Expand menu' }).click();
  expect((await sidebar(page).boundingBox())!.width).toBe(232);
  // Below 1280 there is no choice to make: the rail, and no toggle.
  await page.setViewportSize({ width: 1024, height: 768 });
  await expect(page.getByRole('button', { name: /Collapse menu|Expand menu/ })).toHaveCount(0);
});

for (const [roleName, context] of [['doctor', doctor], ['receptionist', receptionist]] as const)
  test(`at 1024 px the top bar is 48 px and holds clinic, search, bell and avatar only (${roleName})`, async ({ page }) => {
    await signInAs(page, context);
    await page.setViewportSize({ width: 1024, height: 768 });
    await page.goto('/');
    await startPageReady(page);
    const bar = page.getByRole('banner');
    await expect(bar).toContainText('Iyer Family Clinic · Adyar');
    expect((await bar.boundingBox())!.height).toBe(48);
    await expect(bar.getByRole('button').or(bar.getByRole('link'))).toHaveCount(3);
    await expect(bar.getByRole('button', { name: /^Signed in as / })).toBeVisible();
    await expect(bar.getByRole('button', { name: 'Sign out' })).toHaveCount(0);
  });

test('the account menu opens and closes by keyboard and gives focus back to the avatar; the skip link stays first', async ({ page }) => {
  await signInAs(page, receptionist);
  await page.goto('/front-desk');
  const board = page.getByRole('main').getByRole('heading', { name: /^Front desk/ }); // the shell has rendered: a Tab before it lands nowhere
  await expect(board).toBeVisible();
  const avatar = page.getByRole('button', { name: 'Signed in as A. Joseph' });
  await avatar.focus();
  await page.keyboard.press('Enter');
  await expect(page.getByRole('menu')).toBeVisible();
  await expect(page.getByRole('menuitem').last()).toHaveText('Sign out'); // last, separated
  await page.keyboard.press('Escape');
  await expect(page.getByRole('menu')).toHaveCount(0);
  await expect(avatar).toBeFocused();
  await page.reload();
  await expect(board).toBeVisible();
  await page.keyboard.press('Tab');
  await expect(page.getByRole('link', { name: 'Skip to main content' })).toBeFocused();
});

for (const locale of ['en', 'ar'])
  test(`the shell mirrors in Arabic, the search key hint reads left to right, and the open account menu passes axe (${locale})`, async ({ page }) => {
    await signInAs(page, doctor, locale);
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto('/today');
    const avatar = page.getByRole('banner').getByRole('button', { name: locale === 'ar' ? 'تم تسجيل الدخول باسم Dr. Arjun Rao' : 'Signed in as Dr. Arjun Rao' });
    await expect(avatar).toBeVisible();
    const box = (await avatar.boundingBox())!;
    expect(locale === 'ar' ? box.x < 720 : box.x > 720).toBe(true); // the avatar sits at the reading end
    await expect(page.getByRole('banner').locator('kbd')).toHaveAttribute('dir', 'ltr');
    await avatar.click();
    await expect(page.getByRole('menu')).toBeVisible();
    // The open menu is modal (the page behind is hidden from assistive technology), so it is checked on its own; the shell is checked closed above.
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: (c: Element) => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run(document.querySelector('[role=menu]')!)).violations
        .map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
  });
