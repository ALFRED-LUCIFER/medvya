import { expect, test } from '@playwright/test';

// P0 steps that exist today: signed-out home, F-05 system states, locale and right-to-left (signed-in shell: app-shell.spec.ts).
// CI has no API: answer /v1/me/context as "not signed in".
test.beforeEach(({ page }) => page.route('**/v1/me/context', r => r.fulfill({ status: 401 })));

test('home page renders', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
});

test('signed out, an app address offers sign-in and comes back to it (not "Page not found")', async ({ page }) => {
  const record = '/patients/directory/01a1b691-cb4e-7111-93a8-65b798132ce1';
  await page.goto(record);
  await expect(page.getByRole('heading', { name: 'Sign in to Medvya' })).toBeVisible();
  const login = page.waitForRequest(r => r.url().includes('/bff/login'));
  await page.getByRole('button', { name: 'Sign in with a passkey' }).click();
  expect((await login).url()).toContain(`returnUrl=${encodeURIComponent(record)}`);
});

test('switching to Arabic lays the page out right-to-left', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('combobox').click();
  await page.getByRole('option', { name: 'العربية' }).click();
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await expect(page.locator('html')).toHaveAttribute('lang', 'ar');
});

test('going offline shows the offline banner', async ({ page, context }) => {
  await page.goto('/');
  await context.setOffline(true);
  await expect(page.getByRole('status').or(page.getByRole('alert')).first()).toBeVisible();
});

// A screen's code that fails to download (connection lost before it arrived) must not blank the app and take the banner with it.
test('going offline before the sign-in screen has downloaded still shows the offline banner', async ({ page, context }) => {
  let lose!: () => void;
  const lost = new Promise<void>(r => { lose = r; });
  await page.route('**/src/app/SignInRoute.tsx*', async r => { await lost; await r.abort('internetdisconnected'); });
  await page.goto('/');
  await context.setOffline(true);
  const failed = page.waitForEvent('requestfailed', r => r.url().includes('SignInRoute'));
  lose();
  await failed;
  await expect(page.getByRole('heading', { name: 'Loading…' })).toBeHidden(); // the app has dealt with the failed download
  await expect(page.getByRole('status').filter({ hasText: 'You are offline.' })).toBeVisible(); // the offline strip (MDV-P2-056)
});
