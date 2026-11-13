// Reviewer A walkthrough harness: signs in on the throwaway local stack and counts what a user would do.
// Counters: clicks, fields typed, key presses, route changes (pathname changes of the main frame), and how far the page was scrolled.
import { createRequire } from 'node:module';
import { mkdirSync, appendFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
export const root = resolve(here, '..');
const require = createRequire(resolve(here, '../../../tests/e2e/package.json'));
export const { chromium } = require('@playwright/test');
export const origin = 'http://localhost:5391';
export const now = new Date(Date.parse('2026-11-03T04:00:00Z') + 30 * 60_000);
export const users = { doctor: /Dr\. Arjun Rao/, receptionist: /A\. Joseph/, nurse: /S\. Thomas RN/, admin: /K\. Lakshmi/, menon: /Dr\. S\. Menon/ };
export const desktop = { viewport: { width: 1440, height: 900 } };
export const tablet = { viewport: { width: 1024, height: 768 }, isMobile: true, hasTouch: true, deviceScaleFactor: 1 };

export function rand() { return Math.floor(Math.random() * 9000) + 1000; }
export function synthetic(first, last) {
  const n = rand();
  return { name: `${first} ${last}${[...String(n)].map(d => 'abcdefghij'[+d]).join('')}`, mobile: `+91 90000 0${n}`, digits: `0${n}`,
    dob: `${1950 + (n % 50)}-${String(1 + (n % 12)).padStart(2, '0')}-${String(1 + (n % 28)).padStart(2, '0')}` };
}

/** A signed-in page plus a step recorder. `flow` names the screenshot folder: walkthroughs/<role>/<flow>/NN-state.png */
export async function session(browser, user, role, flow, device = desktop) {
  const ctx = await browser.newContext({ ...device, reducedMotion: 'reduce' });
  const page = await ctx.newPage();
  await page.addInitScript(() => localStorage.setItem('medvya.locale', 'en'));
  await page.clock.setFixedTime(now);
  const c = { clicks: 0, fields: 0, keys: 0, routes: 0, routeList: [], scrollPx: 0, apiErrors: [], shots: 0, startedAt: Date.now() };
  let last = '';
  page.on('framenavigated', f => { if (f !== page.mainFrame()) return; const p = new URL(f.url()).pathname + new URL(f.url()).search; if (p !== last) { last = p; c.routes++; c.routeList.push(p.replace(/[0-9a-f]{8}-[0-9a-f-]{27}/g, ':id')); } });
  page.on('response', r => { if (r.status() >= 400 && r.url().includes('/v1/')) c.apiErrors.push(`${r.status()} ${r.request().method()} ${new URL(r.url()).pathname.replace(/[0-9a-f]{8}-[0-9a-f-]{27}/g, ':id')}`); });
  await page.goto(origin + '/app/');
  await page.getByRole('main').getByRole('textbox').fill('staff@example.test');
  await page.getByRole('main').getByRole('textbox').press('Enter');
  await page.getByRole('link', { name: user }).click();
  await page.waitForURL(/\/app\/(?!$)/, { timeout: 20000 });
  await page.waitForLoadState('networkidle').catch(() => {});
  const picker = page.getByText(/\(synthetic\) · /).first();
  if (await picker.isVisible().catch(() => false)) { await picker.click(); await page.waitForLoadState('networkidle').catch(() => {}); }
  await page.waitForTimeout(600);
  // sign-in is not part of the task being measured
  Object.assign(c, { clicks: 0, fields: 0, keys: 0, routes: 0, routeList: [], scrollPx: 0, apiErrors: [], startedAt: Date.now() });
  const dir = `${root}/screenshots/local-stack/walkthroughs/${role}/${flow}`;
  mkdirSync(dir, { recursive: true });
  const log = line => { console.log(line); appendFileSync(`${root}/reviews/reviewer-a-evidence/walkthrough-a.log`, `${role}/${flow} ${line}\n`); };
  const settle = async () => { await page.waitForLoadState('networkidle', { timeout: 8000 }).catch(() => {}); await page.waitForTimeout(350); };
  const api = {
    page, ctx, c, log, settle,
    click: async (loc, what) => { await loc.scrollIntoViewIfNeeded().catch(() => {}); c.scrollPx += await scrolled(page); await loc.click(); c.clicks++; if (what) log(`  click: ${what}`); await settle(); },
    fill: async (loc, v, what) => { await loc.scrollIntoViewIfNeeded().catch(() => {}); c.scrollPx += await scrolled(page); await loc.fill(v); c.fields++; if (what) log(`  type: ${what}`); },
    press: async (loc, key) => { await loc.press(key); c.keys++; await settle(); },
    pick: async (combo, option, what) => { await combo.scrollIntoViewIfNeeded().catch(() => {}); c.scrollPx += await scrolled(page); await combo.click(); c.clicks++; await page.getByRole('option', { name: option }).first().click(); c.clicks++; if (what) log(`  pick: ${what}`); await settle(); },
    goto: async path => { await page.goto(origin + '/app' + path); await settle(); },
    shot: async (state, full = false) => { c.shots++; const f = `${dir}/${String(c.shots).padStart(2, '0')}-${state}.png`; await page.screenshot({ path: f, fullPage: full }); log(`  shot: ${f.replace(root + '/', '')}`); },
    text: async (sel = '#main') => (await page.locator(sel).innerText().catch(() => '')).replace(/\n{2,}/g, '\n'),
    counts: label => { const s = `COUNTS ${label}: clicks=${c.clicks} fields=${c.fields} keys=${c.keys} routeChanges=${c.routes} scrolledPx=${c.scrollPx} apiErrors=${c.apiErrors.length} scriptMs=${Date.now() - c.startedAt}`; log(s); log(`  routes: ${c.routeList.join(' > ')}`); if (c.apiErrors.length) log(`  apiErrors: ${[...new Set(c.apiErrors)].join('; ')}`); return s; },
    reset: () => Object.assign(c, { clicks: 0, fields: 0, keys: 0, routes: 0, routeList: [], scrollPx: 0, apiErrors: [], startedAt: Date.now() }),
    close: () => ctx.close(),
  };
  return api;
}

let lastY = new WeakMap();
async function scrolled(page) {
  const y = await page.evaluate(() => Math.round(scrollY)).catch(() => 0);
  const prev = lastY.get(page) ?? 0; lastY.set(page, y);
  return Math.abs(y - prev);
}

/** Bounding boxes (top, height) of named landmarks: how much of the viewport the chrome takes before the first field. */
export const layout = (page, selectors) => page.evaluate(sel => Object.fromEntries(Object.entries(sel).map(([k, s]) => {
  const e = document.querySelector(s); if (!e) return [k, null]; const r = e.getBoundingClientRect(); return [k, { top: Math.round(r.top + scrollY), h: Math.round(r.height), bottom: Math.round(r.bottom + scrollY) }];
})), selectors);
