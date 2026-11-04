import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';
import { completeWizard, mockOnboarding } from './support/onboarding';

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
async function axe(page: Page) {
  await page.addScriptTag({ path: axePath });
  return page.evaluate(async () =>
    (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
      .map(v => `${v.id} (${v.nodes.length})`));
}

test('a visitor from the region selector signs up: the country is carried into multi-factor sign-in', async ({ page }) => {
  await page.route('**/v1/me/context', r => r.fulfill({ status: 401 }));
  await page.route('**/bff/login**', r => r.fulfill({ body: 'signing in' }));
  await page.goto('/signup?country=AE');
  await expect(page.getByRole('radio', { name: 'United Arab Emirates' })).toBeChecked();
  await page.getByRole('button', { name: 'Create account' }).click();
  await expect(page).toHaveURL('/bff/login?signup=true&returnUrl=%2Fonboarding%3Fcountry%3DAE');
});

test('a solo doctor completes the six steps and lands on the app with the licence notice', async ({ page }) => {
  const api = await mockOnboarding(page);
  await page.goto('/onboarding?country=IN');
  await completeWizard(page);

  // Each Continue saved the draft with the next step; the licence step was checked by the server.
  expect(api.saves.map(s => [s.step, s.validate])).toEqual([[2, 1], [3, 2], [4, 3], [5, 4], [6, 5], [6, 6]]);
  const last = api.saves.at(-1)!.data as { licence: { authority: string; number: string }; hours: { newVisitFee: number; weekdays: unknown[] }; team: unknown[] };
  expect(last.licence).toMatchObject({ authority: 'TNMC', number: '84213' });
  expect(last.hours).toMatchObject({ newVisitFee: 100000, slotMinutes: 20 });
  expect(last.hours.weekdays).toHaveLength(2);
  expect(last.team).toEqual([{ email: 'joseph@example.test', role: 'Receptionist' }]);

  await expect.poll(() => api.chose).toBe('m-1'); // the new practice becomes the working context
  await expect(page.getByText('Verification pending · usually within 1 working day.')).toBeVisible();
});

test('leaving mid-wizard and signing in again resumes at the same step with the saved values', async ({ page }) => {
  await mockOnboarding(page, { step: 3, data: { profile: { fullName: 'Arjun Rao (synthetic)', mode: 'Solo', specialty: 'ENT' }, licence: { authority: 'KMC', number: '1234' },
    clinic: { name: 'Rao Clinic (synthetic)', address: '12 Test Street' } } });
  await page.goto('/onboarding');
  await expect(page.getByRole('heading', { level: 2, name: 'Clinic' })).toBeVisible();
  await expect(page.getByLabel('Clinic name')).toHaveValue('Rao Clinic (synthetic)');
  await page.getByRole('button', { name: 'Back' }).click();
  await expect(page.getByRole('combobox', { name: 'Medical council' })).toContainText('KMC');
  await expect(page.getByLabel('Registration number')).toHaveValue('1234');
});

test('errors appear under the field and are announced; a server error keeps the person on the step', async ({ page }) => {
  await mockOnboarding(page, { step: 1, data: {} });
  await page.goto('/onboarding');
  await page.getByRole('button', { name: 'Continue' }).click();
  await expect(page.getByLabel('Full name')).toHaveAttribute('aria-invalid', 'true');
  await expect(page.locator('#fullName-error')).toHaveText('This is required.');
  await expect(page.getByText('Some fields need your attention. Correct them to continue.')).toBeVisible();

  await page.getByLabel('Full name').fill('Arjun Rao (synthetic)');
  await page.getByLabel('Specialty').fill('ENT');
  await page.getByRole('button', { name: 'Continue' }).click();
  await page.getByRole('combobox', { name: 'Medical council' }).click();
  await page.getByRole('option', { name: /TNMC/ }).click();
  await page.getByLabel('Registration number').fill('1'); // the fake server refuses this one
  await page.getByRole('button', { name: 'Continue' }).click();
  await expect(page.locator('#number-error')).toHaveText('Check the format.');
  await expect(page.getByRole('heading', { level: 2, name: 'Licence' })).toBeVisible();
});

test('an owner who is not a doctor skips the licence step', async ({ page }) => {
  await mockOnboarding(page, { step: 1, data: {} });
  await page.goto('/onboarding');
  await page.getByLabel('I run a clinic and I am not a doctor').check();
  await page.getByLabel('Full name').fill('K. Lakshmi (synthetic)');
  await page.getByRole('button', { name: 'Continue' }).click();
  await expect(page.getByRole('heading', { level: 2, name: 'Clinic' })).toBeVisible();
  await expect(page.getByText('Step 2 of 5: Clinic')).toBeVisible();
});

test('the wizard works from the keyboard at 768 px', async ({ page }) => {
  await page.setViewportSize({ width: 768, height: 1024 });
  await mockOnboarding(page, { step: 1, data: {} });
  await page.goto('/onboarding');
  await page.getByLabel('Full name').focus();
  await page.keyboard.type('Arjun Rao (synthetic)');
  await page.keyboard.press('Tab');
  await page.keyboard.type('ENT');
  await page.keyboard.press('Enter'); // Enter in a field submits the step
  await expect(page.getByRole('heading', { level: 2, name: 'Licence' })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

for (const locale of ['en', 'ar'])
  for (const step of [1, 2, 4]) {
    test(`axe finds no violations on wizard step ${step} (${locale})`, async ({ page }) => {
      await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
      await mockOnboarding(page, { step, data: { profile: { fullName: 'Arjun Rao (synthetic)', mode: 'Solo', specialty: 'ENT' } } });
      await page.goto('/onboarding');
      await expect(page.getByRole('heading', { level: 2 })).toBeVisible();
      if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
      expect(await axe(page)).toEqual([]);
    });
  }
