import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';
import { completeWizard, mockOnboarding } from './support/onboarding';
import { mockSignInStart } from './support/signIn';

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
async function axe(page: Page) {
  await page.addScriptTag({ path: axePath });
  return page.evaluate(async () =>
    (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
      .map(v => `${v.id} (${v.nodes.length})`));
}

const practice = { profile: { fullName: 'Arjun Rao (synthetic)', mode: 'Solo', specialties: ['general-practice'] }, licence: { authority: 'KMC', number: '1234', expiresOn: '2031-12-31' },
  clinic: { name: 'Rao Clinic (synthetic)', address: '12 Test Street, Chennai' } };

test('a visitor from the region selector signs up: the country is carried into multi-factor sign-in', async ({ page }) => {
  await page.route('**/v1/me/context', r => r.fulfill({ status: 401 }));
  const start = await mockSignInStart(page);
  await page.goto('/signup?country=AE');
  await expect(page.getByRole('radio', { name: /United Arab Emirates/ })).toBeChecked();
  await page.getByLabel('Practice name').fill('Lakeside Clinic (synthetic)');
  await page.getByRole('textbox', { name: 'Emirate' }).fill('Abu Dhabi');
  await page.getByRole('button', { name: 'Continue' }).click();
  await page.getByLabel('Full name').fill('Omar Haddad (synthetic)');
  await page.getByLabel('Work email').fill('owner@example.test');
  await page.getByRole('button', { name: 'Continue' }).click();
  await expect(page).toHaveURL(start.providerUrl);
  expect(start.bodies).toEqual([{ loginHint: 'owner@example.test', returnUrl: '/onboarding?country=AE', signup: true }]);
});

test('the create-account answers fill the new draft once, then leave the browser (MDV-P1-059)', async ({ page }) => {
  const api = await mockOnboarding(page);
  const prefill = { profile: { fullName: 'Omar Haddad (synthetic)', mode: 'Clinic' }, clinic: { name: 'Lakeside Clinic (synthetic)', address: 'Abu Dhabi' } };
  await page.addInitScript(p => { if (!sessionStorage.getItem('seeded')) { sessionStorage.setItem('seeded', '1'); sessionStorage.setItem('medvya.signUp', p); } }, JSON.stringify(prefill));
  await page.goto('/onboarding?country=AE');
  await page.getByRole('button', { name: 'Start setup' }).click();
  await expect(page.getByLabel('Full name')).toHaveValue('Omar Haddad (synthetic)');
  expect(api.saves[0]).toMatchObject({ step: 1, data: prefill });
  expect(await page.evaluate(() => sessionStorage.getItem('medvya.signUp'))).toBeNull();
});

test('a solo doctor completes the five steps, sees the ready screen and lands on the app', async ({ page }) => {
  const api = await mockOnboarding(page);
  await page.goto('/onboarding?country=IN');
  await completeWizard(page);

  // Each Continue saved the draft with the next step after the server checked the current one.
  expect(api.saves.map(s => [s.step, s.validate])).toEqual([[2, 1], [3, 2], [4, 3], [5, 4], [5, 5]]);
  const last = api.saves.at(-1)!.data as { profile: { specialties: string[] }; licence: { authority: string; number: string };
    hours: { newVisitFee: number; days: string[]; weekdays: unknown[] }; team: unknown[]; patients: string };
  expect(last.profile.specialties).toEqual(['general-practice']);
  expect(last.licence).toMatchObject({ authority: 'TNMC', number: '84213', attested: true, expiresOn: '2031-12-31' });
  expect(last.hours).toMatchObject({ newVisitFee: 80000, slotMinutes: 15, days: ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'] });
  expect(last.hours.weekdays).toHaveLength(2);
  expect(last.team).toEqual([{ email: 'joseph@example.test', role: 'Receptionist' }]);
  expect(last.patients).toBe('Fresh');

  await expect.poll(() => api.chose).toBe('m-1'); // the new practice becomes the working context
  await expect(page.getByText(/Waiting for approval|being set up/)).toHaveCount(0); // an approved organisation has no notice
});

test('the live preview mirrors what is typed (1280 px)', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await mockOnboarding(page, { step: 1, data: {} });
  await page.goto('/onboarding');
  const preview = page.getByRole('complementary', { name: 'Live preview · your booking page' });
  await page.getByLabel('Practice name').fill('Iyer Family Clinic (synthetic)');
  await page.getByLabel(/^Address/).fill('4 Test Road, Chennai');
  await page.getByLabel('Diabetes').check();
  await expect(preview).toContainText('Iyer Family Clinic (synthetic)');
  await expect(preview).toContainText('Chennai · I am a doctor here');
  await expect(preview).toContainText('Diabetes');
  await expect(preview).toContainText('Mon–Sat · 09:00–13:00 · 17:00–20:00');
  await expect(preview).toContainText('Consultation ₹800.00 · 15 min');
});

test('leaving mid-wizard and signing in again resumes at the same step with the saved values', async ({ page }) => {
  await mockOnboarding(page, { step: 2, data: { ...practice, hours: { weekdays: [{ start: '10:00', end: '14:00' }], saturday: [], slotMinutes: 20, newVisitFee: 50000, followUpFee: 30000, days: ['Monday', 'Sunday'] } } });
  await page.goto('/onboarding');
  await expect(page.getByRole('heading', { level: 1, name: 'When are you open, and what do you charge?' })).toBeVisible();
  await expect(page.getByRole('switch', { name: /Sunday/ })).toBeChecked();
  await expect(page.getByRole('switch', { name: /Tuesday/ })).not.toBeChecked();
  await expect(page.getByRole('radio', { name: '20 minutes' })).toBeChecked();
  await expect(page.getByLabel('New patient fee (INR)')).toHaveValue('500');
  await page.getByRole('button', { name: 'Back' }).click();
  await expect(page.getByLabel('Practice name')).toHaveValue('Rao Clinic (synthetic)');
  await expect(page.getByRole('combobox', { name: 'Medical council' })).toContainText('KMC');
  await expect(page.getByLabel('Registration number')).toHaveValue('1234');
});

test('a finish whose answer was lost succeeds on the second try with the same practice (#205)', async ({ page }) => {
  const api = await mockOnboarding(page, null, { loseFirstFinish: true });
  await page.goto('/onboarding?country=IN');
  await completeWizard(page, { lost: true });
  expect(api.finishes).toBe(2);
  await expect.poll(() => api.chose).toBe('m-1');
});

test('errors appear under the field and are announced; a server error keeps the person on the step', async ({ page }) => {
  await mockOnboarding(page, { step: 1, data: {} });
  await page.goto('/onboarding');
  await page.getByRole('button', { name: 'Continue' }).click();
  await expect(page.getByLabel('Full name')).toHaveAttribute('aria-invalid', 'true');
  await expect(page.locator('#fullName-error')).toHaveText('This is required.');
  await expect(page.locator('#specialties-error')).toHaveText('This is required.');
  await expect(page.getByText('Some fields need your attention. Correct them to continue.')).toBeVisible();

  await page.getByLabel('Full name').fill('Arjun Rao (synthetic)');
  await page.getByLabel('Practice name').fill('Rao Clinic (synthetic)');
  await page.getByLabel('A clinic').check();
  await page.getByLabel('Email Medvya writes to about this request').fill('desk@example.test');
  await page.getByLabel(/^Address/).fill('12 Test Street');
  await page.getByLabel('Paediatrics').check();
  await page.getByRole('combobox', { name: 'Medical council' }).click();
  await page.getByRole('option', { name: /TNMC/ }).click();
  await page.getByLabel('Registration number').fill('1'); // the fake server refuses this one
  await page.getByLabel('Registration expiry date').fill('2031-12-31');
  await page.getByLabel('I confirm this registration is my own and that it is valid.').check();
  await page.getByRole('button', { name: 'Continue' }).click();
  await expect(page.locator('#number-error')).toHaveText('Check the format.');
  await expect(page.getByRole('heading', { level: 1, name: 'Tell us about your practice.' })).toBeVisible();
});

test('an owner who is not a doctor gives no licence and needs no specialty', async ({ page }) => {
  await mockOnboarding(page, { step: 1, data: {} });
  await page.goto('/onboarding');
  await page.getByLabel('I manage the clinic').check();
  await expect(page.getByRole('combobox', { name: 'Medical council' })).toHaveCount(0);
  await page.getByLabel('Full name').fill('K. Lakshmi (synthetic)');
  await page.getByLabel('Practice name').fill('Lakshmi Clinic (synthetic)');
  await page.getByLabel('A clinic').check();
  await page.getByLabel('Email Medvya writes to about this request').fill('desk@example.test');
  await page.getByLabel(/^Address/).fill('1 Test Road');
  await page.getByRole('button', { name: 'Continue' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'When are you open, and what do you charge?' })).toBeVisible();
});

test('the team step invites the front desk, says what each role sees, and does not lose an address typed but not invited', async ({ page }) => {
  const api = await mockOnboarding(page, { step: 3, data: practice });
  await page.goto('/onboarding');
  await expect(page.getByText('Each role sees only what it needs. Front desk never sees clinical notes.')).toBeVisible();
  await expect(page.getByText('Booking, check-in, billing. No clinical notes.')).toBeVisible();
  await page.getByLabel('Email address').fill('nurse@example.test');
  await page.getByRole('button', { name: 'Continue' }).click();
  await expect(page.locator('#inviteEmail-error')).toHaveText('Press Invite to add this person, or clear the field.');

  await page.getByRole('radio', { name: 'Nurse' }).check();
  await page.getByLabel('Email address').press('Enter'); // Enter in the address invites
  await page.getByLabel('Email address').fill('NURSE@example.test');
  await page.getByRole('button', { name: 'Invite', exact: true }).click();
  await expect(page.locator('#inviteEmail-error')).toHaveText('Each person can be invited once.');
  await page.getByLabel('Email address').fill('');
  await page.getByRole('button', { name: 'Continue' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Bring your existing patients.' })).toBeVisible();
  expect(api.saves.at(-1)!.data.team).toEqual([{ email: 'nurse@example.test', role: 'Nurse' }]);
});

test('skip leaves a step empty; finish later saves and signs out', async ({ page }) => {
  const api = await mockOnboarding(page, { step: 4, data: { ...practice, patients: 'Move' } });
  await page.goto('/onboarding');
  await expect(page.getByLabel('Move from my software')).toBeChecked();
  await page.getByRole('button', { name: 'Skip for now' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Get paid and keep patients informed.' })).toBeVisible();
  expect(api.saves.at(-1)!.data.patients).toBeUndefined();
  await expect(page.getByText('Later, in Settings')).toHaveCount(4);

  await page.getByRole('button', { name: 'Finish later' }).click();
  await expect.poll(() => api.signedOut).toBe(true);
  expect(api.saves.at(-1)!.step).toBe(5); // signing in again resumes here
});

test('the wizard works from the keyboard at 768 px', async ({ page }) => {
  await page.setViewportSize({ width: 768, height: 1024 });
  await mockOnboarding(page, { step: 1, data: { profile: { mode: 'Clinic' }, clinic: { type: 'Clinic' } } });
  await page.goto('/onboarding');
  await page.getByLabel('Full name').focus();
  await page.keyboard.type('K. Lakshmi (synthetic)');
  await page.keyboard.press('Tab');
  await page.keyboard.type('Lakshmi Clinic (synthetic)');
  await page.keyboard.press('Tab');
  await page.keyboard.type('1 Test Road');
  await page.keyboard.press('Tab');
  await page.keyboard.type('desk@example.test');
  await page.keyboard.press('Enter'); // Enter in a field submits the step
  await expect(page.getByRole('heading', { level: 1, name: 'When are you open, and what do you charge?' })).toBeVisible();
  await page.getByRole('switch', { name: /Sunday/ }).focus();
  await page.keyboard.press('Space');
  await expect(page.getByRole('switch', { name: /Sunday/ })).toBeChecked();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  for (const target of await page.locator('main button, main input[type=radio], main input[type=checkbox]').all()) {
    const box = (await target.evaluate(e => (e.closest('label') ?? e).getBoundingClientRect().height));
    expect(box).toBeGreaterThanOrEqual(44);
  }
});

for (const locale of ['en', 'hi', 'ar'])
  for (const step of [1, 2, 3, 4, 5]) {
    test(`axe finds no violations on wizard step ${step} (${locale})`, async ({ page }) => {
      await page.setViewportSize({ width: 1280, height: 900 });
      await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
      await mockOnboarding(page, { step, data: { ...practice, team: [{ email: 'joseph@example.test', role: 'Receptionist' }], patients: 'Upload' } });
      await page.goto('/onboarding');
      await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
      await expect(page.locator('html')).toHaveAttribute('dir', locale === 'ar' ? 'rtl' : 'ltr');
      if (locale === 'hi') await expect(page.locator('html')).toHaveAttribute('lang', /^hi/);
      expect(await axe(page)).toEqual([]);
    });
  }

test('axe finds no violations on the ready screen (ar)', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('medvya.locale', 'ar'));
  await mockOnboarding(page, { step: 5, data: practice });
  await page.goto('/onboarding');
  await page.getByRole('button', { name: 'أنهِ التجهيز' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Rao Clinic (synthetic) جاهزة.' })).toBeVisible();
  expect(await axe(page)).toEqual([]);
});
