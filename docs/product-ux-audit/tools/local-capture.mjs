// Audit evidence capture against the throwaway local full stack (tests/e2e/fullstack/stack.mjs: real Host.Api + PostgreSQL, dev sign-in, synthetic cast).
// Read-only: signs in as each synthetic role, opens every route at each viewport, saves screenshots and layout/axe metrics.
// Run: node docs/product-ux-audit/tools/local-capture.mjs [discover|capture] [role,role] [width,width]
import { createRequire } from 'node:module';
import { appendFileSync, mkdirSync, readFileSync, writeFileSync, existsSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(here, '..');
const require = createRequire(resolve(here, '../../../tests/e2e/package.json'));
const { chromium } = require('@playwright/test');
const axeSource = readFileSync(require.resolve('axe-core/axe.min.js'), 'utf8');

const origin = 'http://localhost:5391';
const now = new Date(Date.parse('2026-11-03T04:00:00Z') + 30 * 60_000); // the stack's clock + 30 min, as the full-stack specs do
const viewports = {
  375: { viewport: { width: 375, height: 812 }, isMobile: true, hasTouch: true, deviceScaleFactor: 1 },
  768: { viewport: { width: 768, height: 1024 }, isMobile: true, hasTouch: true, deviceScaleFactor: 1 },
  1024: { viewport: { width: 1024, height: 768 }, isMobile: true, hasTouch: true, deviceScaleFactor: 1 },
  1440: { viewport: { width: 1440, height: 900 } },
  1920: { viewport: { width: 1920, height: 1080 } },
};
const roles = {
  doctor: /Dr\. Arjun Rao/, receptionist: /A\. Joseph/, nurse: /S\. Thomas RN/, 'practice-admin': /K\. Lakshmi/,
  'care-coordinator': /Priya N\./, 'hospital-doctor': /Dr\. S\. Menon/, 'hospital-it': /R\. Varghese/,
};

async function signIn(context, user, locale = 'en') {
  const page = await context.newPage();
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.addInitScript(axeSource);
  await page.clock.setFixedTime(now);
  await page.goto(origin + '/app/');
  await page.getByRole('main').getByRole('textbox').fill('staff@example.test');
  await page.getByRole('main').getByRole('textbox').press('Enter');
  await page.getByRole('link', { name: user }).click();
  await page.waitForURL(/\/app\/(?!$)/, { timeout: 20000 });
  await page.waitForLoadState('networkidle').catch(() => {});
  const picker = page.getByText(/\(synthetic\) · /).first();
  if (await picker.isVisible().catch(() => false)) { await picker.click(); await page.waitForLoadState('networkidle').catch(() => {}); }
  await page.waitForTimeout(800);
  return page;
}

const settle = async page => {
  await page.waitForLoadState('networkidle', { timeout: 10000 }).catch(() => {});
  await page.getByText('Loading…', { exact: true }).first().waitFor({ state: 'hidden', timeout: 10000 }).catch(() => {});
  await page.waitForTimeout(500);
};

const measure = page => page.evaluate(async runAxe => {
  const vis = e => { const r = e.getBoundingClientRect(); const s = getComputedStyle(e); return r.width > 0 && r.height > 0 && s.visibility !== 'hidden'; };
  const main = document.getElementById('main') || document.querySelector('main');
  const h1 = document.querySelector('h1');
  const targets = [...document.querySelectorAll('a[href],button,input:not([type=hidden]),select,textarea,[role=tab],[role=switch],[role=combobox],[role=radio],[role=checkbox]')].filter(vis);
  const box = e => e.getBoundingClientRect();
  const label = e => (e.getAttribute('aria-label') || e.textContent || e.getAttribute('name') || e.tagName).trim().replace(/\s+/g, ' ').slice(0, 32);
  const under24 = targets.filter(e => box(e).width < 24 || box(e).height < 24);
  const under44 = targets.filter(e => box(e).height < 44 || box(e).width < 44);
  const header = document.querySelector('header');
  const firstField = main && main.querySelector('input,textarea,select,[role=combobox],article,table,[role=listitem],li');
  const fonts = {};
  if (main) for (const e of main.querySelectorAll('*')) if ([...e.childNodes].some(n => n.nodeType === 3 && n.textContent.trim()) && vis(e)) { const f = getComputedStyle(e).fontSize; fonts[f] = (fonts[f] || 0) + 1; }
  const widest = main ? Math.max(0, ...[...main.querySelectorAll('p')].filter(vis).map(p => Math.round(box(p).width))) : 0;
  let axe = null;
  if (runAxe && window.axe) { try { axe = (await window.axe.run()).violations.map(v => `${v.id}:${v.impact}:${v.nodes.length}`); } catch (e) { axe = ['axe-error ' + e.message]; } }
  return {
    h1: h1 ? h1.textContent.trim().slice(0, 80) : null, lang: document.documentElement.lang, dir: document.documentElement.dir,
    docH: document.documentElement.scrollHeight, docW: document.documentElement.scrollWidth, vw: innerWidth, vh: innerHeight,
    headerH: header ? Math.round(box(header).height) : null, mainTop: main ? Math.round(box(main).top) : null,
    h1Top: h1 ? Math.round(box(h1).top) : null, firstContentTop: firstField ? Math.round(box(firstField).top + scrollY) : null,
    targets: targets.length, under24: under24.length, under24Sample: under24.slice(0, 6).map(e => `${label(e)} ${Math.round(box(e).width)}x${Math.round(box(e).height)}`),
    under44: under44.length, under44Sample: under44.slice(0, 6).map(e => `${label(e)} ${Math.round(box(e).width)}x${Math.round(box(e).height)}`),
    hOverflow: document.documentElement.scrollWidth > innerWidth + 1, fonts, widestParagraph: widest,
    buttons: [...document.querySelectorAll('main button, main a[href]')].filter(vis).map(label).slice(0, 40),
    words: (main ? main.innerText : document.body.innerText).trim().split(/\s+/).length, axe,
  };
}, true);

async function shoot(page, role, slug, width, state, path) {
  const errors = [];
  const dir = `${root}/screenshots/local-stack/${role}/${slug}`;
  mkdirSync(dir, { recursive: true });
  await page.screenshot({ path: `${dir}/${width}-${state}.png` });
  await page.screenshot({ path: `${dir}/${width}-${state}-full.png`, fullPage: true });
  const m = await measure(page).catch(e => ({ error: String(e).slice(0, 200) }));
  const rec = { env: 'local-stack', role, slug, path, state, width, finalUrl: page.url().replace(origin, ''), at: new Date().toISOString(), errors, ...m };
  appendFileSync(`${root}/evidence/local-metrics.jsonl`, JSON.stringify(rec) + '\n');
  return rec;
}

/** Ids for the dynamic routes, read from the doctor's own screens (never from the database). */
async function discover(browser) {
  const ctx = await browser.newContext(viewports[1440]);
  const page = await signIn(ctx, roles.doctor);
  const out = { consults: [], patients: [] };
  await page.goto(origin + '/app/today'); await settle(page);
  out.todayText = (await page.locator('#main').innerText()).slice(0, 1500);
  out.consults = await page.evaluate(() => [...new Set([...document.querySelectorAll('a[href*="/consult/"]')].map(a => a.getAttribute('href').replace('/app', '')))]);
  await page.goto(origin + '/app/patients/directory'); await settle(page);
  out.directoryText = (await page.locator('#main').innerText()).slice(0, 1500);
  out.patients = await page.evaluate(() => [...new Set([...document.querySelectorAll('a[href*="/patients/"]')].map(a => a.getAttribute('href').replace('/app', '')))]);
  await page.goto(origin + '/app/front-desk'); await settle(page);
  out.boardText = (await page.locator('#main').innerText()).slice(0, 2500);
  out.boardLinks = await page.evaluate(() => [...new Set([...document.querySelectorAll('#main a[href]')].map(a => a.getAttribute('href').replace('/app', '') + ' | ' + a.textContent.trim().slice(0, 40)))]);
  for (const [role, user] of Object.entries(roles)) {
    const c = await browser.newContext(viewports[1440]);
    const p = await signIn(c, user).catch(e => null);
    if (!p) { out[role] = 'sign-in failed'; continue; }
    out[role] = { landing: p.url().replace(origin, ''), nav: await p.evaluate(() => [...new Set([...document.querySelectorAll('nav a[href], aside a[href]')].map(a => a.getAttribute('href').replace('/app', '')))]) };
    await c.close();
  }
  writeFileSync(`${root}/evidence/local-discovery.json`, JSON.stringify(out, null, 2));
  console.log(JSON.stringify(out, null, 1));
  await ctx.close();
}

async function capture(browser, onlyRoles, onlyWidths) {
  const plan = JSON.parse(readFileSync(`${here}/local-routes.json`, 'utf8'));
  for (const role of onlyRoles ?? Object.keys(plan)) {
    for (const width of onlyWidths ?? Object.keys(viewports)) {
      const ctx = await browser.newContext({ ...viewports[width], reducedMotion: 'reduce' });
      let page;
      try { page = await signIn(ctx, roles[role]); } catch (e) { console.log(`${role} ${width}: sign-in failed ${String(e).slice(0, 120)}`); await ctx.close(); continue; }
      const errors = [];
      page.on('console', m => { if (m.type() === 'error') errors.push(m.text().slice(0, 160)); });
      page.on('response', r => { if (r.status() >= 400 && r.url().includes('/v1/')) errors.push(`${r.status()} ${r.request().method()} ${new URL(r.url()).pathname}`); });
      for (const [slug, path] of plan[role]) {
        errors.length = 0;
        const t0 = Date.now();
        await page.goto(origin + '/app' + path, { waitUntil: 'domcontentloaded' }).catch(() => {});
        await settle(page);
        const rec = await shoot(page, role, slug, width, 'default', path);
        rec.errors.push(...new Set(errors));
        console.log(`${role}|${width}|${slug}|${rec.finalUrl}|h1=${rec.h1}|docH=${rec.docH}|ovf=${rec.hOverflow}|<24=${rec.under24}|<44=${rec.under44}/${rec.targets}|axe=${(rec.axe || []).join(',')}|${Date.now() - t0}ms|${[...new Set(errors)].join(';')}`);
      }
      await ctx.close();
    }
  }
}

const [mode = 'discover', r, w] = process.argv.slice(2);
if (!existsSync(`${root}/evidence`)) mkdirSync(`${root}/evidence`, { recursive: true });
const browser = await chromium.launch();
try { if (mode === 'discover') await discover(browser); else await capture(browser, r && r !== '-' ? r.split(',') : null, w ? w.split(',') : null); }
finally { await browser.close(); }
