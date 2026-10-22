import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P0-012 against a mocked API (CI has none): facility picker, switching, F-05a "Session expired", nothing sensitive in browser storage.
const memberships = [
  { id: 'm-adyar', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-adyar', facilityName: 'Adyar', role: 'SoloDoctor' },
  { id: 'm-besant', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-besant', facilityName: 'Besant Nagar', role: 'HospitalDoctor' },
];
const user = { id: 'u-rao', displayName: 'Dr. Arjun Rao' };
const active = (id: string) => ({
  membershipId: id, tenantId: 't-1', facilityId: id === 'm-adyar' ? 'f-adyar' : 'f-besant', flags: [],
  role: id === 'm-adyar' ? 'SoloDoctor' : 'HospitalDoctor',
  capabilities: id === 'm-adyar' ? { RegisterBookPatients: 'Full', Patient360Chart: 'Full', StaffRolesIntegrations: 'Config' } : { RegisterBookPatients: 'Full', Patient360Chart: 'Full' },
});

/** A tiny stand-in for the session endpoints; `state.expired` makes every call answer 401 like an idle-timed-out session. */
async function mockServer(page: Page, locale = 'en') {
  const state = { chosen: null as string | null, expired: false, posts: [] as unknown[] };
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => state.expired ? r.fulfill({ status: 401 })
    : r.fulfill({ json: { user, memberships, active: state.chosen ? active(state.chosen) : null } }));
  await page.route('**/v1/me/facility', async r => {
    state.posts.push({ body: r.request().postDataJSON(), csrf: r.request().headers()['x-medvya-csrf'] });
    state.chosen = r.request().postDataJSON().membershipId;
    await r.fulfill({ status: 204 });
  });
  return state;
}

test('two memberships: choose a facility, then switch without signing in again', async ({ page }) => {
  const server = await mockServer(page);
  await page.goto('/schedule');
  await expect(page).toHaveURL('/choose-facility');
  await expect(page.getByRole('heading', { name: 'Choose where you are working today' })).toBeVisible();
  await expect(page.getByRole('button', { name: /Besant Nagar/ })).toContainText('Hospital doctor');
  await expect(page.getByRole('button', { name: 'Keep current facility' })).toHaveCount(0); // nothing to keep yet

  await page.getByRole('button', { name: /Adyar/ }).click();
  await expect(page).toHaveURL('/schedule'); // back to where the user was going
  expect(server.posts).toEqual([{ body: { membershipId: 'm-adyar' }, csrf: '1' }]);
  await expect(page.getByRole('navigation').first()).toContainText('Practice settings');

  await page.getByRole('link', { name: 'Change facility' }).click();
  await page.getByRole('button', { name: /Besant Nagar/ }).click();
  await expect(page.getByRole('navigation').first()).toContainText('Hospital doctor');
  await expect(page.getByRole('navigation').first()).not.toContainText('Practice settings');
});

for (const locale of ['en', 'ar'])
  test(`an ended session shows "Session expired" and survives a reload (${locale})`, async ({ page }) => {
    const server = await mockServer(page, locale);
    server.chosen = 'm-adyar';
    await page.clock.install();
    await page.goto('/schedule');
    await expect(page.getByRole('navigation').first()).toBeVisible();

    server.expired = true; // e.g. idle timeout on the server
    await page.clock.fastForward(61_000); // the one-minute status poll sees the 401, with no user action
    await expect(page.getByRole('alert')).toContainText(locale === 'en' ? 'Session expired' : 'انتهت الجلسة');
    await page.reload();
    await expect(page.getByRole('alert')).toBeVisible();
    await expect(page).toHaveURL('/schedule'); // signing in again returns here

    await page.addScriptTag({ path: createRequire(import.meta.url).resolve('axe-core/axe.min.js') });
    const violations = await page.evaluate(async () => (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string }[] }> } }).axe.run()).violations.map(v => v.id));
    expect(violations).toEqual([]);

    const login = page.waitForRequest(r => r.url().includes('/bff/login'));
    await page.getByRole('alert').getByRole('button').click();
    expect(new URL((await login).url()).searchParams.get('returnUrl')).toBe('/schedule');
  });

test('browser storage holds no patient or session data, only allowlisted keys', async ({ page }) => {
  const server = await mockServer(page);
  server.chosen = 'm-adyar';
  await page.goto('/schedule');
  await expect(page.getByRole('navigation').first()).toBeVisible();
  const storage = await page.evaluate(() => ({ local: { ...localStorage }, session: { ...sessionStorage } }));
  expect(storage).toEqual({ local: { 'medvya.locale': 'en' }, session: { 'medvya.hadSession': '1' } });
});
