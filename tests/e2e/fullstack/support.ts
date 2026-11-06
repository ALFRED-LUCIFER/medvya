import { createRequire } from 'node:module';
import { expect, type Page } from '@playwright/test';

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');

/** The browser's clock, 30 minutes into the API's clock (E2E_CLOCK_START): every offered slot is still in the future for both. */
export const browserNow = () => new Date(Date.parse(process.env.E2E_CLOCK_START!) + 30 * 60_000);

/** Signs in through the real BFF and the development identity provider, then picks the clinic when asked. Leaves the page on the home screen. */
export async function signIn(page: Page, user: RegExp, locale: 'en' | 'ar' = 'en') {
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.addInitScript({ path: axePath }); // an init script is not subject to the page's strict CSP, which stays on
  await page.clock.setFixedTime(browserNow());
  await page.goto('/app/');
  await page.getByRole('main').getByRole('button').first().click(); // the only button on the signed-out home screen
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
