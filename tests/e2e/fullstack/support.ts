import { createRequire } from 'node:module';
import { expect, type Browser, type Page } from '@playwright/test';

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');

/** The browser's clock, 30 minutes into the API's clock (E2E_CLOCK_START): every offered slot is still in the future for both. */
export const browserNow = () => new Date(Date.parse(process.env.E2E_CLOCK_START!) + 30 * 60_000);

/** Signs in through the real BFF and the development identity provider, then picks the clinic when asked. Leaves the page on the home screen. */
export async function signIn(page: Page, user: RegExp, locale: 'en' | 'ar' = 'en') {
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.addInitScript({ path: axePath }); // an init script is not subject to the page's strict CSP, which stays on
  await page.clock.setFixedTime(browserNow());
  await page.goto('/app/');
  // The sign-in start (MDV-P1-059): the email is only a hint, and the development provider still lists its synthetic users.
  await page.getByRole('main').getByRole('textbox').fill('staff@example.test');
  await page.getByRole('main').getByRole('textbox').press('Enter');
  await page.getByRole('link', { name: user }).click();
  await page.waitForURL(/\/app\/(choose-facility|front-desk|schedule)/);
  if (page.url().includes('choose-facility')) await page.getByText(/\(synthetic\) · /).first().click();
  await expect(page.getByRole('navigation').first()).toBeVisible();
}

export async function expectNoAxeViolations(page: Page) {
  await page.evaluate(() => Promise.all(document.getAnimations().map(a => a.finished.catch(() => undefined)))); // a cancelled animation rejects: it is over, which is all we wait for // axe must not sample a colour mid-transition
  const violations = await page.evaluate(async () =>
    (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
      .map(v => `${v.id} (${v.nodes.length})`));
  expect(violations).toEqual([]);
}

/**
 * Collects every URL the page asks for (navigations, scripts and API calls). Patient data may only travel in bodies:
 * `leaks(words)` returns the URLs that contain any of the given words (case-insensitive, also URL-encoded).
 */
export function watchUrls(page: Page) {
  const urls: string[] = [];
  page.on('request', r => urls.push(r.url()));
  page.on('framenavigated', f => urls.push(f.url()));
  return {
    urls,
    leaks: (words: string[]) => urls.filter(u => { const d = decodeURIComponent(u).toLowerCase(); return words.some(w => d.includes(w.toLowerCase())); }),
  };
}

/** A synthetic patient unique to this run (a kept database never flags it as a duplicate): letters for digits in the surname, +91 90000 0xxxx. */
export function syntheticPatient(first: string, last: string) {
  const n = Math.floor(Math.random() * 9000) + 1000;
  return {
    name: `${first} ${last}${[...String(n)].map(d => 'abcdefghij'[+d]).join('')}`, mobile: `+91 90000 0${n}`,
    dob: `${1950 + (n % 50)}-${String(1 + (n % 12)).padStart(2, '0')}-${String(1 + (n % 28)).padStart(2, '0')}`,
  };
}

/** From the board: Walk-in → quick registration (five fields) → check in with the given doctor. Leaves the page on the walk-in success screen. */
export async function walkIn(page: Page, patient: ReturnType<typeof syntheticPatient>, doctor: RegExp) {
  await page.goto('/app/front-desk');
  await page.getByRole('link', { name: 'Walk-in' }).click();
  await page.getByRole('link', { name: 'New patient: quick registration' }).click();
  await page.getByLabel('Full name').fill(patient.name);
  await page.getByLabel('Mobile').fill(patient.mobile);
  await page.getByRole('textbox', { name: 'Date of birth', exact: true }).fill(patient.dob);
  await page.getByRole('radio', { name: 'Female', exact: true }).check();
  await page.getByRole('button', { name: 'Register patient' }).click();
  await page.getByRole('link', { name: 'Check in now (walk-in)' }).click();
  await expect(page.getByText(`Patient: ${patient.name} · MRN`)).toBeVisible();
  await page.getByRole('combobox', { name: 'Doctor' }).click();
  await page.getByRole('option', { name: doctor }).click();
  await page.getByRole('button', { name: 'Check in now' }).click();
  await expect(page.getByRole('heading', { name: `${patient.name} is checked in` })).toBeVisible();
}

/** The owner switches nurse intake on or off in Settings (audited, MDV-P2-007), in a page of its own. Only an owner may. */
export async function setNurseIntake(browser: Browser, enabled: boolean) {
  const admin = await browser.newPage();
  try {
    await signIn(admin, /K\. Lakshmi/);
    await admin.goto('/app/settings?tab=staff');
    const toggle = admin.getByRole('region', { name: 'Nurse intake' }).getByRole('switch', { name: /^Nurse intake before the doctor/ });
    await expect(toggle).toBeVisible();
    if ((await toggle.getAttribute('aria-checked')) !== String(enabled)) await toggle.click();
    await expect(toggle).toHaveAttribute('aria-checked', String(enabled)); // shown from the server's answer, so the switch is saved
  } finally {
    await admin.close();
  }
}

/** The same screen for the same user in Arabic: right to left and axe-clean. A page of its own, since signIn pins its page's locale. */
export async function expectArabicAxeClean(browser: Browser, user: RegExp, path: string) {
  const page = await browser.newPage();
  try {
    await signIn(page, user, 'ar');
    await page.goto(path);
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await expect(page.getByRole('main')).toBeVisible();
    await page.waitForLoadState('networkidle');
    await expectNoAxeViolations(page);
  } finally {
    await page.close();
  }
}
