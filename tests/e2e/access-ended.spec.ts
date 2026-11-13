import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P0-015 F-05b against a mocked API (the RLS rule behind `accessEnded` is in MembershipDirectoryTests on real PostgreSQL).
// A member removed from their only clinic sees "no access", not the setup wizard; a person who never had a clinic still gets the wizard.
const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
const user = { id: 'u-joseph', displayName: 'A. Joseph (synthetic)' };

async function signIn(page: Page, context: object, locale: string) {
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: context }));
}

for (const [locale, title, rtl] of [['en', 'You don’t have access to this facility.', false], ['ar', 'ليس لديك صلاحية الوصول إلى هذه المنشأة.', true]] as const) {
  test(`removed from the only clinic: F-05b, not the setup wizard (${locale})`, async ({ page }) => {
    await signIn(page, { user, memberships: [], accessEnded: true, active: null }, locale);
    await page.goto('/');
    await expect(page.getByRole('heading', { name: title })).toBeVisible();
    await expect(page.getByRole('alert')).toBeVisible();
    await expect(page).not.toHaveURL(/onboarding/);
    expect(await page.locator('html').getAttribute('dir')).toBe(rtl ? 'rtl' : 'ltr');
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string }[] }> } }).axe.run()).violations.map(v => v.id));
    expect(violations).toEqual([]);
  });
}

test('a person who never had a clinic still gets the setup wizard', async ({ page }) => {
  await signIn(page, { user, memberships: [], accessEnded: false, active: null }, 'en');
  await page.goto('/');
  await expect(page).toHaveURL(/onboarding/);
});
