import { expect, test } from '@playwright/test';
import { expectNoAxeViolations, signIn, watchUrls } from './support';

// MDV-P0-029: the refusals, through the real stack (cookie session, policies, EF filters and SQL row-level security). Synthetic cast:
// docs/00-overview/personas.md. Iyer Family Clinic (India) and Al Noor Family Clinic (UAE) are two tenants.
const meeraId = '5e1d0001-0004-4000-8000-000000000001'; // Iyer Family Clinic's patient
const adyarId = '5e1d0001-0001-4000-8000-000000000001'; // Iyer Family Clinic's facility
const csrf = { 'X-Medvya-Csrf': '1' };

test('a second tenant\'s user cannot see the first tenant\'s patients, schedule or board', async ({ page }) => {
  const watch = watchUrls(page);
  await signIn(page, /Omar Haddad/);
  await page.goto('/app/patients/directory');
  const search = page.getByRole('searchbox', { name: 'Name, MRN, phone or ID' });

  // Control: the same search finds the user's own patient, so an empty answer below means isolation, not a broken search.
  await search.fill('Fatima');
  await search.press('Enter');
  await expect(page.getByRole('link', { name: /Fatima .*, \d+ y, Female/ })).toBeVisible();

  await search.fill('Meera');
  await search.press('Enter');
  await expect(page.getByRole('status')).toHaveText('No patients match.');

  // Straight at the API with the same session: the other tenant's rows do not exist for this user.
  const record = await page.request.get(`/v1/patients/${meeraId}`);
  expect([403, 404]).toContain(record.status());
  expect(await record.text()).not.toContain('Meera');
  const board = await page.request.get(`/v1/front-desk/board?facilityId=${adyarId}`);
  expect([403, 404]).toContain(board.status());
  const day = await page.request.get(`/v1/schedule?facilityId=${adyarId}`);
  expect([403, 404]).toContain(day.status());
  const lookup = await page.request.post('/v1/patients/search', { headers: csrf, data: { query: 'Meera' } });
  expect(await lookup.text()).not.toContain('Meera');
  expect(watch.leaks(['Meera', 'Fatima'])).toEqual([]);
});

test('a receptionist is refused administration and the audit log, in the API and in the app', async ({ page }) => {
  await signIn(page, /A\. Joseph/);
  for (const path of ['/v1/staff', '/v1/settings/doctors', '/v1/audit/access-log'])
    expect((await page.request.get(path)).status(), path).toBe(403);
  await expect(page.getByRole('navigation').getByRole('link', { name: /Staff|Settings|Audit/ })).toHaveCount(0);
  await page.goto('/app/admin/staff');
  await expect(page.getByRole('heading', { name: /not found/i })).toBeVisible();
});

// MDV-P1-096: a refused screen says so; the server's 403 behind it is unchanged.
const encounterId = '00000000-0000-4000-8000-000000000096'; // any id: the capability check refuses before any lookup
test('a receptionist on Today, Settings or a consultation is told her role does not include the screen; the API still answers 403', async ({ page }) => {
  await signIn(page, /A\. Joseph/);
  for (const path of ['/v1/notes/pending', `/v1/encounters/${encounterId}/note`, '/v1/settings/doctors'])
    expect((await page.request.get(path)).status(), path).toBe(403);
  for (const path of ['/app/today', '/app/settings', `/app/consult/${encounterId}`]) {
    await page.goto(path);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Screen not available');
    await expect(page.getByRole('main')).toContainText('Your role does not include this screen.');
  }
  await page.getByRole('button', { name: 'Go to my start page' }).click();
  await expect(page).toHaveURL(/\/app\/front-desk$/);
});

test('a nurse opening a consultation sees one calm message and no error lines; the note API still answers 403', async ({ page }) => {
  await signIn(page, /S\. Thomas/);
  expect((await page.request.get(`/v1/encounters/${encounterId}/note`)).status()).toBe(403);
  await page.goto(`/app/consult/${encounterId}`);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Screen not available');
  await expect(page.getByRole('main').getByRole('alert')).toHaveCount(0);
  await expectNoAxeViolations(page);
});

test('the practice administrator is allowed what the receptionist is refused (the control for the test above)', async ({ page }) => {
  await signIn(page, /K\. Lakshmi/);
  expect((await page.request.get('/v1/staff')).status()).toBe(200);
});

// Arabic, right to left: the main screens of the journey, each axe-clean.
test('the front-desk screens are right-to-left in Arabic and axe-clean', async ({ page }) => {
  await signIn(page, /A\. Joseph/, 'ar');
  for (const path of ['/app/front-desk', '/app/schedule', '/app/patients/new', '/app/patients/directory']) {
    await page.goto(path);
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await expect(page.locator('html')).toHaveAttribute('lang', 'ar');
    await expect(page.getByRole('main')).toBeVisible();
    await expectNoAxeViolations(page);
  }
  await page.goto('/app/schedule');
  await page.getByRole('list').getByRole('button').first().click(); // opens a booking panel
  await expect(page.getByRole('complementary')).toBeVisible();
  await expectNoAxeViolations(page);
});
