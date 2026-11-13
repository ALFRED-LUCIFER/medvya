// Reviewer B: read-only check of the settled focus ring (after the 150 ms colour transition) and the notification panel.
// Run: node docs/product-ux-audit/tools/states-focus.mjs
import { createRequire } from 'node:module';
import { mkdirSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
const here = dirname(fileURLToPath(import.meta.url)); const root = resolve(here, '..');
const require = createRequire(resolve(here, '../../../tests/e2e/package.json'));
const { chromium } = require('@playwright/test');
const origin = 'http://localhost:5391'; const now = new Date(Date.parse('2026-11-03T04:00:00Z') + 30 * 60_000);
const browser = await chromium.launch();
const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 } });
const page = await ctx.newPage();
await page.addInitScript(() => localStorage.setItem('medvya.locale', 'en'));
await page.clock.setFixedTime(now);
await page.goto(origin + '/app/');
await page.getByRole('main').getByRole('textbox').fill('staff@example.test');
await page.getByRole('main').getByRole('textbox').press('Enter');
await page.getByRole('link', { name: /Dr\. Arjun Rao/ }).click();
await page.waitForURL(/\/app\/(?!$)/); await page.waitForLoadState('networkidle').catch(() => {});
const picker = page.getByText(/\(synthetic\) · /).first(); if (await picker.isVisible().catch(() => false)) await picker.click();
await page.waitForTimeout(800);
const dir = `${root}/screenshots/local-stack/states/focus`; mkdirSync(dir, { recursive: true });
await page.goto(origin + '/app/front-desk'); await page.waitForLoadState('networkidle').catch(() => {}); await page.waitForTimeout(600);
const info = () => page.evaluate(() => { const e = document.activeElement, s = getComputedStyle(e), r = e.getBoundingClientRect(); return { label: (e.getAttribute('aria-label') || e.textContent).trim().slice(0, 30), variant: e.dataset.variant, outline: `${s.outlineStyle} ${s.outlineWidth} ${s.outlineColor} offset ${s.outlineOffset}`, box: [r.x, r.y, r.width, r.height].map(Math.round) }; });
for (let i = 1; i <= 24; i++) {
  await page.keyboard.press('Tab'); await page.waitForTimeout(450);
  const f = await info(); console.log(i, JSON.stringify(f));
  if (i >= 13) { const [x, y, w, h] = f.box; await page.screenshot({ path: `${dir}/1440-settled-tab${i}.png`, clip: { x: Math.max(0, x - 40), y: Math.max(0, y - 30), width: Math.min(700, w + 80), height: Math.min(400, h + 60) } }).catch(() => {}); }
}
await page.goto(origin + '/app/today'); await page.waitForLoadState('networkidle').catch(() => {}); await page.waitForTimeout(500);
const bell = page.locator('div > header').first().getByRole('button').last();
console.log('bell', await bell.getAttribute('aria-label'), await bell.isDisabled());
await bell.click({ timeout: 4000 }).catch(e => console.log('bell click failed', String(e).slice(0, 100)));
await page.waitForTimeout(600);
mkdirSync(`${root}/screenshots/local-stack/states/notifications`, { recursive: true });
await page.screenshot({ path: `${root}/screenshots/local-stack/states/notifications/1440-open.png` });
await browser.close();
