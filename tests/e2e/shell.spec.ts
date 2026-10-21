import { expect, test } from '@playwright/test';

// P0 steps that exist today: signed-out home, F-05 system states, locale and right-to-left (signed-in shell: app-shell.spec.ts).
// CI has no API: answer /v1/me/context as "not signed in".
test.beforeEach(({ page }) => page.route('**/v1/me/context', r => r.fulfill({ status: 401 })));

test('home page renders', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
});

test('unknown address shows the not-found state and leads back home', async ({ page }) => {
  await page.goto('/no-such-page');
  await page.getByRole('button').click();
  await expect(page).toHaveURL('/');
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
