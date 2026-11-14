import { expect, test, type Page } from '@playwright/test';
import { mockNurse } from './support/nurse';
import { mockProblems } from './support/problems';
import { mockReconciliation } from './support/reconciliation';

// MDV-P1-092 AC2: on the medicines and problems sub-pages and nurse intake, at 1024 touch, the patient strip stays pinned under the top bar
// while the page is scrolled to its last field, and names the same patient. The consultation itself is in consult-studio.spec.ts.
test.use({ viewport: { width: 1024, height: 768 }, hasTouch: true });

const scrolledToEnd = async (page: Page) => {
  const strip = page.locator('[data-slot=patient-banner]');
  await expect(strip.getByRole('heading', { name: 'Meera Iyer (synthetic)', level: 1 })).toBeVisible();
  await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight));
  // A page that fits the screen has nothing to scroll; any longer one must have scrolled.
  await expect.poll(() => page.evaluate(() => window.scrollY > 0 || document.documentElement.scrollHeight <= innerHeight)).toBe(true);
  return strip;
};

test('the strip stays on the problems page', async ({ page }) => {
  await mockProblems(page);
  await page.goto('/consult/e-1/problems');
  const strip = await scrolledToEnd(page);
  await expect(strip.getByRole('heading', { name: 'Meera Iyer (synthetic)' })).toBeInViewport({ ratio: 1 });
});

test('the strip stays on the medicines page', async ({ page }) => {
  await mockReconciliation(page);
  await page.goto('/consult/e-1/medicines');
  const strip = await scrolledToEnd(page);
  await expect(page.getByRole('button', { name: 'Confirm changes' })).toBeInViewport();
  await expect(strip.getByRole('heading', { name: 'Meera Iyer (synthetic)' })).toBeInViewport({ ratio: 1 });
});

test('the strip stays on nurse intake, with the allergy as recorded so far', async ({ page }) => {
  await mockNurse(page);
  await page.goto('/nurse/a1');
  const strip = await scrolledToEnd(page);
  await expect(page.getByRole('button', { name: 'Hand off · Ready for doctor' })).toBeInViewport();
  await expect(strip.getByRole('heading', { name: 'Meera Iyer (synthetic)' })).toBeInViewport({ ratio: 1 });
  await expect(strip.locator('[data-slot=allergy], [data-slot=allergy-state]').first()).toBeInViewport();
});
