import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';
import { mockSignInStart } from './support/signIn';

// MDV-P1-059 against a mocked API: the Medvya-owned screens of the 6 Nov sign-in flow. The provider's pages (code, passkey, MFA) are not ours.
const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
async function axe(page: Page) {
  await page.addScriptTag({ path: axePath });
  return page.evaluate(async () =>
    (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
      .map(v => `${v.id} (${v.nodes.length})`));
}

const signedOut = async (page: Page, locale = 'en') => {
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ status: 401 }));
  return mockSignInStart(page);
};

test('the work email goes to the provider as a login hint, and nothing else is kept', async ({ page }) => {
  const start = await signedOut(page);
  await page.goto('/');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Sign in to Medvya');
  await page.getByLabel('Work email').fill('reception.desk@example.test');
  await page.getByLabel('Work email').press('Enter');
  await expect(page).toHaveURL(start.providerUrl);
  expect(start.bodies).toEqual([{ loginHint: 'reception.desk@example.test', returnUrl: '/' }]);
  expect(start.leaks('example.test')).toEqual([]); // in the POST body only, never in a Medvya URL
  expect(await page.evaluate(() => JSON.stringify({ ...localStorage, ...sessionStorage }))).not.toContain('example.test');
});

test('a malformed email is announced and does not leave the page', async ({ page }) => {
  await signedOut(page);
  await page.goto('/');
  await page.getByLabel('Work email').fill('reception');
  await page.getByRole('button', { name: 'Continue' }).click();
  await expect(page.getByRole('alert')).toHaveText('Enter your work email, for example name@clinic.in.');
  await expect(page.getByLabel('Work email')).toHaveAttribute('aria-invalid', 'true');
  await expect(page.getByLabel('Work email')).toBeFocused();
  await expect(page).toHaveURL('/');
});

test('passkey and organisation sign-in start the provider sign-in without a hint', async ({ page }) => {
  await signedOut(page);
  await page.goto('/');
  await page.getByRole('button', { name: 'Sign in with a passkey' }).click();
  await expect(page).toHaveURL('/bff/login?returnUrl=%2F');
});

test('a refused sign-in start comes back with the generic message', async ({ page }) => {
  await signedOut(page);
  await page.route('**/bff/login', r => r.fulfill({ status: 429 }));
  await page.goto('/');
  await page.getByLabel('Work email').fill('reception.desk@example.test');
  await page.getByRole('button', { name: 'Continue' }).click();
  await expect(page).toHaveURL('/?signin=failed');
  await expect(page.getByRole('alert')).toContainText('Sign-in did not complete');
});

test('a refused sign-in says so without the reason', async ({ page }) => {
  await signedOut(page);
  await page.goto('/?signin=failed');
  await expect(page.getByRole('alert')).toContainText('Sign-in did not complete');
});

// #912: the provider signed the person in without a second factor; the page says what is missing and where to add it.
for (const locale of ['en', 'ar'])
  test(`a sign-in without a second factor says how to add one (${locale})`, async ({ page }) => {
    await signedOut(page, locale);
    await page.goto('/?signin=mfa');
    const alert = page.getByRole('alert');
    await expect(alert).toContainText(locale === 'ar' ? 'خطوة ثانية' : 'second sign-in step');
    await expect(alert.getByRole('link')).toHaveAttribute('href', 'https://aka.ms/mysecurityinfo');
    await expect(alert).not.toContainText(locale === 'ar' ? 'لم يكتمل تسجيل الدخول' : 'Sign-in did not complete');
    if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    expect(await axe(page)).toEqual([]);
  });

test('create account: two steps feed the setup, the email becomes the login hint', async ({ page }) => {
  const start = await signedOut(page);
  await page.goto('/');
  await page.getByRole('link', { name: 'Create a practice' }).click();
  await expect(page).toHaveURL('/signup');
  // The sign-in page also has a Continue button: wait for the sign-up step before pressing it.
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Your practice');
  await page.getByRole('button', { name: 'Continue' }).click();
  await expect(page.getByText('Fill this in to continue.')).toHaveCount(2); // practice name and city
  await page.getByRole('radio', { name: /United Arab Emirates/ }).check();
  await page.getByLabel('Practice name').fill('Lakeside Clinic (synthetic)');
  await page.getByRole('textbox', { name: 'Emirate' }).fill('Abu Dhabi');
  await page.getByRole('button', { name: 'Continue' }).click();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('About you');
  await expect(page.getByLabel('Full name')).toBeFocused();
  await page.getByLabel('Full name').fill('Omar Haddad (synthetic)');
  await page.getByLabel('Work email').fill('owner@example.test');
  await page.getByRole('radio', { name: 'A clinic manager' }).check();
  await page.getByRole('button', { name: 'Continue' }).click();
  await expect(page).toHaveURL(start.providerUrl);
  expect(start.bodies).toEqual([{ loginHint: 'owner@example.test', returnUrl: '/onboarding?country=AE', signup: true }]);
  expect(start.leaks('example.test')).toEqual([]);
});

test('choose location: the last used facility is first and marked in words; "Not you?" signs out', async ({ page }) => {
  const membership = (id: string, facilityName: string) => ({ id, tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: `f-${id}`, facilityName, role: 'Receptionist' });
  let signedIn = true;
  await page.route('**/v1/me/context', r => signedIn
    ? r.fulfill({ json: { user: { id: 'u-1', displayName: 'A. Joseph (synthetic)' }, memberships: [membership('m-adyar', 'Adyar'), membership('m-velachery', 'Velachery')], lastMembershipId: 'm-velachery', active: null } })
    : r.fulfill({ status: 401 }));
  await page.route('**/bff/logout', r => { signedIn = false; return r.fulfill({ status: 204 }); });
  await page.goto('/schedule');
  await expect(page).toHaveURL('/choose-facility');
  const cards = page.getByRole('main').getByRole('listitem');
  await expect(cards.first()).toContainText('Velachery');
  await expect(cards.first()).toContainText('Last used');
  await expect(cards.nth(1)).not.toContainText('Last used');
  await expect(page.getByText('Signed in as A. Joseph (synthetic)')).toBeVisible();
  expect(await axe(page)).toEqual([]);
  await page.getByRole('button', { name: 'Not you? Sign out' }).click();
  await expect(page).toHaveURL('/');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Sign in to Medvya');
});

// #912: sign-out also ends the provider session, so a shared front-desk PC does not sign the last account straight back in.
test('sign out goes on to the provider sign-out address the server returns', async ({ page }) => {
  const membership = (id: string, facilityName: string) => ({ id, tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: `f-${id}`, facilityName, role: 'Receptionist' });
  // Two clinics, so the picker shows (with one, the clinic is chosen automatically, UX-08).
  await page.route('**/v1/me/context', r => r.fulfill({ json: { user: { id: 'u-1', displayName: 'A. Joseph (synthetic)' }, memberships: [membership('m-1', 'Adyar'), membership('m-2', 'Velachery')], active: null } }));
  await page.route('**/bff/logout', r => r.fulfill({ json: { location: '/provider/logout' } }));
  await page.route('**/provider/logout', r => r.fulfill({ body: 'provider signed out' }));
  await page.goto('/choose-facility');
  await page.getByRole('button', { name: 'Not you? Sign out' }).click();
  await expect(page).toHaveURL('/provider/logout');
});

test('signed out: "Not you?" leads to the sign-in start instead of signing the old account in again', async ({ page }) => {
  await signedOut(page);
  await page.addInitScript(() => sessionStorage.setItem('medvya.hadSession', '1'));
  await page.goto('/schedule');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('You were signed out');
  await page.getByRole('button', { name: 'Not you? Use another account' }).click();
  await expect(page).toHaveURL('/');
  await expect(page.getByLabel('Work email')).toBeVisible();
});

for (const locale of ['en', 'hi', 'ar'])
  for (const width of [360, 1280])
    test(`sign-in, create account and signed-out screens are axe clean (${locale}, ${width} px)`, async ({ page }) => {
      await page.setViewportSize({ width, height: 800 });
      await signedOut(page, locale);
      await page.goto('/');
      await expect(page.getByLabel(locale === 'ar' ? 'بريد العمل' : locale === 'hi' ? 'कार्य ईमेल' : 'Work email')).toBeVisible();
      if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true); // no sideways scroll
      expect(await axe(page)).toEqual([]);
      await page.goto('/signup');
      await expect(page.getByRole('radiogroup').or(page.getByRole('group')).first()).toBeVisible();
      expect(await axe(page)).toEqual([]);
      await page.evaluate(() => sessionStorage.setItem('medvya.hadSession', '1'));
      await page.goto('/schedule');
      await expect(page.getByRole('alert')).toBeVisible();
      expect(await axe(page)).toEqual([]);
    });

test('keyboard: the sign-in start is reachable in order and every target is at least 44 px', async ({ page }) => {
  await signedOut(page);
  await page.goto('/');
  await page.getByLabel('Work email').focus();
  for (const name of ['Continue', 'Sign in with a passkey', 'Hospital staff: organisation sign-in', 'Create a practice']) {
    await page.keyboard.press('Tab');
    await expect(page.getByRole(name === 'Create a practice' ? 'link' : 'button', { name })).toBeFocused();
  }
  for (const target of await page.getByRole('main').locator('button, a, input').all())
    expect((await target.boundingBox())!.height, await target.textContent() ?? '').toBeGreaterThanOrEqual(44);
});
