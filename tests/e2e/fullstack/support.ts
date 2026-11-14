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
  // With one clinic the app chooses it itself (UX-08) and the picker never shows, so wait for either, and click only a picker that is there.
  const picker = page.getByText(/\(synthetic\) · /).first();
  const navigation = page.getByRole('navigation').first();
  await expect(picker.or(navigation).first()).toBeVisible();
  if (await picker.isVisible()) await picker.click();
  await expect(navigation).toBeVisible();
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
    // Not checked: the build's hashed bundle names (/app/assets/, "trash-….js" holds "rash") and the sign-in redirect's random protocol values
    // (state, nonce, code: base64 that can hold a short word by chance). Everything else in every URL is.
    leaks: (words: string[]) => urls.filter(u => {
      const url = new URL(u);
      if (url.pathname.startsWith('/app/assets/')) return false;
      for (const k of ['state', 'nonce', 'code', 'code_challenge']) url.searchParams.delete(k);
      const d = decodeURIComponent(url.href).toLowerCase();
      return words.some(w => d.includes(w.toLowerCase()));
    }),
  };
}

/**
 * MDV-P2-047 (AC4): the addresses of a step-up round trip that hold more than opaque identifiers. Every path segment and query value must be
 * one token (an id, a fixed protocol word, or a random protocol value such as state, nonce or code); `redirect_uri` is checked the same way,
 * `scope` word by word, and `resume` may only be a word from the app's list. A name, a date of birth or free text cannot pass (no spaces,
 * no `@`, no `:` outside the origin), and `leaks` above catches a single-word value such as a surname.
 */
export function notOpaque(urls: string[]) {
  const token = /^[A-Za-z0-9_.~-]+$/;
  const opaque = (u: string): boolean => {
    const url = new URL(u, 'http://localhost');
    return url.pathname.split('/').every(s => s === '' || token.test(s)) && url.hash === ''
      && [...url.searchParams].every(([k, v]) => k === 'resume' ? ['authorise', 'sign'].includes(v)
        : k === 'redirect_uri' ? opaque(v) : k === 'scope' ? v.split(' ').every(w => token.test(w)) : token.test(k) && token.test(v));
  };
  return urls.filter(u => !opaque(u));
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
  await page.getByRole('textbox', { name: 'Date of birth (required)', exact: true }).fill(patient.dob);
  await page.getByRole('radio', { name: 'Female', exact: true }).check();
  await page.getByRole('button', { name: 'Register patient' }).click();
  await page.getByRole('link', { name: 'Check in now (walk-in)' }).click();
  await expect(page.getByText(`Patient: ${patient.name} · MRN`)).toBeVisible();
  await page.getByRole('combobox', { name: 'Doctor' }).click();
  await page.getByRole('option', { name: doctor }).click();
  await page.getByRole('button', { name: 'Check in now' }).click();
  await expect(page.getByRole('heading', { name: `${patient.name} is checked in` })).toBeVisible();
}

/**
 * In an open booking panel: waits for the slot hold, finds the patient, picks the result, waits until the pick took, sets the reason.
 * The hold arriving late redraws the panel, so typing before it can lose the search or the pick.
 */
export async function choosePatient(page: Page, search: string, pick: RegExp) {
  const panel = page.getByRole('complementary', { name: 'Book appointment' });
  await expect(panel).toContainText('Held for you until');
  await panel.getByRole('searchbox', { name: 'Name, MRN, phone or ID' }).fill(search);
  await panel.getByRole('button', { name: 'Search' }).click();
  await panel.getByRole('button', { name: pick }).click();
  await expect(panel).toContainText(' · MRN');
  await panel.getByRole('combobox', { name: 'Reason category' }).click();
  await page.getByRole('option', { name: 'Long-term condition review' }).click();
  return panel;
}

/** The owner switches nurse intake on or off in Settings (audited, MDV-P2-007), in a page of its own. Only an owner may. */
export async function setNurseIntake(browser: Browser, enabled: boolean) {
  const admin = await browser.newPage();
  try {
    await signIn(admin, /K\. Lakshmi/);
    await admin.goto('/app/settings?tab=nurse');
    const toggle = admin.getByRole('region', { name: 'Nurse intake' }).getByRole('switch', { name: /^Nurse intake before the doctor/ });
    await expect(toggle).toBeVisible();
    if ((await toggle.getAttribute('aria-checked')) !== String(enabled)) {
      await toggle.click(); // it changes every board, so it asks first (MDV-P2-055)
      await admin.getByRole('dialog').getByRole('button', { name: enabled ? 'Switch on' : 'Switch off', exact: true }).click();
    }
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
