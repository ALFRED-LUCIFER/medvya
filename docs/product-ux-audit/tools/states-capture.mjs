// Reviewer B (visual and interaction): read-only screenshots of interaction states the main capture did not cover.
// Run: node docs/product-ux-audit/tools/states-capture.mjs   → docs/product-ux-audit/screenshots/local-stack/states/<topic>/<width>-<state>.png
// Never clicks a control that writes clinic data: only Tab, hover, opening menus/dialogs, submitting an EMPTY registration step (client-side validation), and simulated slow/failed GETs.
import { createRequire } from 'node:module';
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(here, '..');
const require = createRequire(resolve(here, '../../../tests/e2e/package.json'));
const { chromium } = require('@playwright/test');
const origin = 'http://localhost:5391';
const now = new Date(Date.parse('2026-11-03T04:00:00Z') + 30 * 60_000);
const P = '5e1d0001-0004-4000-8000-000000000001', C = '01a1d400-8c17-7d5e-94c3-40c10b20ce3e';
const notes = [];
const only = process.argv[2]?.split(','); const run = k => !only || only.includes(k); // sections: desktop,loading,small,locale,zoom,nurse
const note = (k, v) => { notes.push({ k, v }); console.log(k, JSON.stringify(v).slice(0, 400)); };

async function signIn(browser, { w, h, touch = false, locale = 'en', user = /Dr\. Arjun Rao/, dsf = 1, reduced = false }) {
  const ctx = await browser.newContext({ viewport: { width: w, height: h }, isMobile: touch, hasTouch: touch, deviceScaleFactor: dsf, ...(reduced && { reducedMotion: 'reduce' }) });
  const page = await ctx.newPage();
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
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
  return { ctx, page };
}
const go = async (page, path) => { await page.goto(origin + '/app' + path, { waitUntil: 'domcontentloaded' }).catch(() => {}); await page.waitForLoadState('networkidle', { timeout: 10000 }).catch(() => {}); await page.waitForTimeout(600); };
const shot = async (page, topic, name, full = false) => { const dir = `${root}/screenshots/local-stack/states/${topic}`; mkdirSync(dir, { recursive: true }); await page.screenshot({ path: `${dir}/${name}.png`, fullPage: full }).catch(e => console.log('shot failed', topic, name, String(e).slice(0, 80))); };
const step = async (name, f) => { try { await f(); } catch (e) { note('FAILED ' + name, String(e).slice(0, 200)); } };
const focusInfo = page => page.evaluate(() => { const e = document.activeElement; if (!e || e === document.body) return null; const s = getComputedStyle(e), r = e.getBoundingClientRect(); return { tag: e.tagName, label: (e.getAttribute('aria-label') || e.textContent || '').trim().slice(0, 30), outline: `${s.outlineStyle} ${s.outlineWidth} ${s.outlineColor} offset ${s.outlineOffset}`, shadow: s.boxShadow.slice(0, 60), top: Math.round(r.top), bottom: Math.round(r.bottom) }; });

const browser = await chromium.launch();
try {
  // ---- desktop 1440: focus, hover, menus, dialogs, validation, gallery, loading, error
  {
    const { ctx, page } = await signIn(browser, { w: 1440, h: 900 });
    if (run('desktop')) { await step('focus', async () => {
      await go(page, '/front-desk');
      const seq = [];
      for (let i = 1; i <= 16; i++) { await page.keyboard.press('Tab'); const f = await focusInfo(page); seq.push(f && `${f.tag}:${f.label}`); if ([1, 2, 4, 9, 13, 16].includes(i)) { await shot(page, 'focus', `1440-front-desk-tab${String(i).padStart(2, '0')}`); note(`focus tab${i}`, f); } }
      note('tab order front-desk', seq);
      await go(page, '/today'); for (let i = 0; i < 14; i++) await page.keyboard.press('Tab'); await shot(page, 'focus', '1440-today-tab14'); note('focus today tab14', await focusInfo(page));
      await go(page, '/patients/new'); await page.locator('#main input').first().focus(); await shot(page, 'focus', '1440-register-input'); note('focus input', await focusInfo(page));
      await page.keyboard.press('Tab'); await page.keyboard.press('Tab'); await shot(page, 'focus', '1440-register-radio'); note('focus radio', await focusInfo(page));
    });
    await step('consult focus obscured', async () => {
      await go(page, `/consult/${C}`);
      const res = [];
      for (let i = 0; i < 70; i++) {
        await page.keyboard.press('Tab');
        const r = await page.evaluate(() => { const e = document.activeElement; if (!e || !document.getElementById('main').contains(e)) return null; const b = e.getBoundingClientRect(); const bars = [...document.querySelectorAll('#main *, header, nav')].filter(x => ['sticky', 'fixed'].includes(getComputedStyle(x).position) && !x.contains(e) && x.getBoundingClientRect().height > 0); let hidden = 0; for (const x of bars) { const q = x.getBoundingClientRect(); const ov = Math.max(0, Math.min(b.bottom, q.bottom) - Math.max(b.top, q.top)) * (Math.min(b.right, q.right) > Math.max(b.left, q.left) ? 1 : 0); hidden = Math.max(hidden, ov / Math.max(1, b.height)); } return { label: (e.getAttribute('aria-label') || e.textContent || e.tagName).trim().slice(0, 28), tag: e.tagName, hidden: +hidden.toFixed(2), top: Math.round(b.top) }; });
        if (r) res.push(r);
        if (r && r.hidden >= 0.5 && res.filter(x => x.hidden >= 0.5).length <= 2) await shot(page, 'focus', `1440-consult-obscured-${res.length}`);
      }
      note('consult focus obscured 1440', { tabbed: res.length, partly: res.filter(x => x.hidden > 0 && x.hidden < 1).length, fully: res.filter(x => x.hidden >= 1).length, samples: res.filter(x => x.hidden > 0).slice(0, 8) });
    });
    await step('hover', async () => {
      await go(page, '/front-desk');
      await page.locator('#main [data-variant=default]').first().hover(); await shot(page, 'hover', '1440-primary-hover');
      await page.locator('#main [data-variant=outline]').first().hover(); await shot(page, 'hover', '1440-outline-hover');
      await page.locator('#main article').first().hover(); await shot(page, 'hover', '1440-board-card-hover');
      await go(page, '/patients/directory'); await page.locator('tbody tr').nth(1).hover(); await shot(page, 'hover', '1440-table-row-hover');
      await page.locator('nav a').nth(2).hover(); await shot(page, 'hover', '1440-nav-hover');
    });
    await step('search', async () => {
      await go(page, '/today'); await page.keyboard.press('Control+k'); await page.waitForTimeout(400); await shot(page, 'global-search', '1440-open');
      await page.keyboard.type('me', { delay: 60 }); await page.waitForTimeout(1200); await shot(page, 'global-search', '1440-results');
      await page.keyboard.type('zzzz', { delay: 30 }); await page.waitForTimeout(1200); await shot(page, 'global-search', '1440-no-results');
      note('search dialog', await page.evaluate(() => { const d = document.querySelector('[role=dialog]'); if (!d) return null; const s = getComputedStyle(d), r = d.getBoundingClientRect(); return { w: Math.round(r.width), top: Math.round(r.top), animation: s.animationName, transition: s.transitionDuration, radius: s.borderRadius, shadow: s.boxShadow.slice(0, 50) }; }));
      await page.keyboard.press('Escape');
    });
    await step('notifications', async () => { await go(page, '/today'); await page.locator('header button').last().click(); await page.waitForTimeout(500); await shot(page, 'notifications', '1440-open'); await page.keyboard.press('Escape'); });
    await step('select', async () => { await go(page, '/today'); await page.locator('header [role=combobox]').first().click(); await page.waitForTimeout(400); await shot(page, 'select', '1440-language-open'); await page.keyboard.press('Escape'); await go(page, '/front-desk/walk-in'); await page.locator('#main [role=combobox]').first().click(); await page.waitForTimeout(400); await shot(page, 'select', '1440-walkin-doctor-open'); note('select content', await page.evaluate(() => { const d = document.querySelector('[data-slot=select-content]'); if (!d) return null; const s = getComputedStyle(d); return { animation: s.animationName, duration: s.animationDuration, easing: s.animationTimingFunction, items: [...d.querySelectorAll('[role=option]')].map(o => Math.round(o.getBoundingClientRect().height)).slice(0, 3) }; })); await page.keyboard.press('Escape'); });
    await step('validation', async () => {
      await go(page, '/patients/new'); await page.getByRole('button', { name: /^Continue$/ }).click(); await page.waitForTimeout(500); await shot(page, 'validation', '1440-register-empty-submit'); await shot(page, 'validation', '1440-register-empty-submit-full', true);
      note('validation', await page.evaluate(() => ({ alerts: [...document.querySelectorAll('#main [role=alert], #main [aria-invalid=true]')].map(e => `${e.tagName}:${(e.textContent || e.getAttribute('name') || '').trim().slice(0, 50)}:${getComputedStyle(e).color}:${getComputedStyle(e).borderColor}:${getComputedStyle(e).fontSize}`).slice(0, 10), focused: document.activeElement?.tagName + ':' + (document.activeElement?.getAttribute('name') || document.activeElement?.id), summary: !!document.querySelector('#main [role=alert] a, #main [aria-labelledby*=error]') })));
      await go(page, '/patients/new?mode=quick'); const b = page.locator('#main [data-variant=default]').last(); note('quick submit label', await b.textContent()); // not clicked: quick mode could save
    });
    await step('schedule panel', async () => { await go(page, '/schedule'); await page.getByText('Meera Iyer (synthetic)').first().click(); await page.waitForTimeout(700); await shot(page, 'schedule-panel', '1440-appointment-open'); });
    await step('gallery', async () => { await go(page, '/__ui'); await shot(page, 'ui-gallery', '1440-default', true); note('gallery h1', await page.locator('h1').first().textContent().catch(() => null)); });
    } if (run('loading')) { await step('loading', async () => {
      const slow = /\/v1\/(?!session|auth|me\b|hubs)/;
      await page.route('**/v1/**', async r => { if (r.request().method() === 'GET' && slow.test(r.request().url())) await new Promise(x => setTimeout(x, 2500)); await r.continue().catch(() => {}); });
      for (const [slug, path] of [['front-desk', '/front-desk'], ['today', '/today'], ['consult', `/consult/${C}`], ['patient-360', `/patients/${P}`], ['settings', '/settings?tab=profile'], ['schedule', '/schedule'], ['directory', '/patients/directory']]) {
        await page.goto(origin + '/app' + path, { waitUntil: 'domcontentloaded' }).catch(() => {}); await page.waitForTimeout(900); await shot(page, 'loading', `1440-${slug}`);
        note('loading ' + slug, await page.evaluate(() => ({ text: document.getElementById('main')?.innerText.trim().slice(0, 80), busy: !!document.querySelector('[aria-busy=true],[role=status]'), h: document.documentElement.scrollHeight })));
        await page.waitForTimeout(4500);
        note('loaded ' + slug, await page.evaluate(() => ({ h: document.documentElement.scrollHeight })));
      }
      await page.unroute('**/v1/**');
    });
    await step('error', async () => {
      await page.route('**/v1/**', r => r.request().method() === 'GET' && /\/v1\/(appointments|encounters|schedule|chart|patients|notes|board|front-desk|today|visits|queue)/.test(r.request().url()) ? r.fulfill({ status: 500, body: '{}', contentType: 'application/json' }).catch(() => {}) : r.continue().catch(() => {}));
      for (const [slug, path] of [['front-desk', '/front-desk'], ['today', '/today'], ['patient-360', `/patients/${P}`], ['schedule', '/schedule']]) { await page.goto(origin + '/app' + path, { waitUntil: 'domcontentloaded' }).catch(() => {}); await page.waitForTimeout(6000); await shot(page, 'error', `1440-${slug}-500`); }
      await page.unroute('**/v1/**');
      await go(page, '/front-desk'); await ctx.setOffline(true); await page.waitForTimeout(2500); await shot(page, 'error', '1440-front-desk-offline'); await ctx.setOffline(false);
    }); }
    await ctx.close();
  }
  // ---- tablet 1024 and phone 375: validation, search, schedule drawer, focus not obscured
  if (run('small')) for (const [w, h] of [[1024, 768], [375, 812]]) {
    const { ctx, page } = await signIn(browser, { w, h, touch: true });
    await step('validation ' + w, async () => { await go(page, '/patients/new'); await page.getByRole('button', { name: /^Continue$/ }).click(); await page.waitForTimeout(500); await shot(page, 'validation', `${w}-register-empty-submit`); await shot(page, 'validation', `${w}-register-empty-submit-full`, true); });
    await step('search ' + w, async () => { await go(page, '/today'); await page.locator('header button, header a').first().click(); await page.waitForTimeout(500); await shot(page, 'global-search', `${w}-open`); await page.keyboard.press('Escape'); });
    await step('notifications ' + w, async () => { await go(page, '/today'); await page.locator('header button').last().click(); await page.waitForTimeout(500); await shot(page, 'notifications', `${w}-open`); await page.keyboard.press('Escape'); });
    await step('schedule panel ' + w, async () => { await go(page, '/schedule'); await page.getByText('Meera Iyer (synthetic)').first().click(); await page.waitForTimeout(700); await shot(page, 'schedule-panel', `${w}-appointment-open`); });
    await step('more ' + w, async () => { await go(page, '/more'); await shot(page, 'more', `${w}-default`, true); });
    await ctx.close();
  }
  // ---- Arabic (RTL) and Hindi
  if (run('locale')) for (const locale of ['ar', 'hi']) for (const [w, h, touch] of [[1440, 900, false], [1024, 768, true], [375, 812, true]]) {
    const { ctx, page } = await signIn(browser, { w, h, touch, locale }).catch(e => { note('FAILED sign-in ' + locale + w, String(e).slice(0, 150)); return {}; });
    if (!page) continue;
    for (const [slug, path] of [['today', '/today'], ['front-desk', '/front-desk'], ['consult', `/consult/${C}`], ['register-patient', '/patients/new'], ['schedule', '/schedule'], ['patients-directory', '/patients/directory'], ['patient-360', `/patients/${P}`], ['settings-hours', '/settings?tab=hours']]) {
      await go(page, path); await shot(page, locale === 'ar' ? 'rtl-arabic' : 'hindi', `${w}-${slug}`);
      note(`${locale} ${w} ${slug}`, await page.evaluate(() => { const own = e => [...e.childNodes].filter(n => n.nodeType === 3).map(n => n.textContent).join('').trim(); const main = document.getElementById('main'); const els = [...main.querySelectorAll('*')].filter(e => own(e) && e.getBoundingClientRect().width > 0);
        const latinOnly = els.filter(e => /^[\x20-\x7E·…–—]+$/.test(own(e)) && /[A-Za-z]{4,}/.test(own(e))).map(own); const lines = e => { const r = document.createRange(); r.selectNodeContents(e); return new Set([...r.getClientRects()].filter(x => x.width > 1).map(x => Math.round(x.top))).size; };
        return { dir: document.documentElement.dir, lang: document.documentElement.lang, hOverflow: document.documentElement.scrollWidth > innerWidth + 1, docH: document.documentElement.scrollHeight, untranslated: [...new Set(latinOnly)].slice(0, 10), untranslatedN: new Set(latinOnly).size, textN: els.length, h1Lines: document.querySelector('h1') ? lines(document.querySelector('h1')) : null, font: getComputedStyle(document.querySelector('h1') || main).fontFamily.slice(0, 60), wrappedButtons: [...main.querySelectorAll('[data-slot=button]')].filter(b => b.getBoundingClientRect().height > 50).length,
          spill: [...main.querySelectorAll('article *')].filter(e => own(e) && (e.getBoundingClientRect().right > e.closest('article').getBoundingClientRect().right + 1 || e.getBoundingClientRect().left < e.closest('article').getBoundingClientRect().left - 1)).length }; }));
    }
    await ctx.close();
  }
  // ---- 200% and 400% zoom equivalents (WCAG 1.4.4 / 1.4.10: 1280 CSS px wide at 400% = 320 px)
  if (run('zoom')) for (const [w, h, name] of [[720, 450, 'zoom200-of-1440x900'], [640, 512, 'zoom200-of-1280x1024'], [320, 256, 'zoom400-of-1280x1024']]) {
    const { ctx, page } = await signIn(browser, { w, h, dsf: 2 }).catch(e => { note('FAILED sign-in zoom ' + w, String(e).slice(0, 150)); return {}; });
    if (!page) continue;
    for (const [slug, path] of [['today', '/today'], ['front-desk', '/front-desk'], ['consult', `/consult/${C}`], ['register-patient', '/patients/new'], ['schedule', '/schedule'], ['patient-360', `/patients/${P}`]]) {
      await go(page, path); await shot(page, 'zoom', `${name}-${slug}`);
      note(`zoom ${w} ${slug}`, await page.evaluate(() => ({ hOverflow: document.documentElement.scrollWidth > innerWidth + 1, docW: document.documentElement.scrollWidth, vw: innerWidth, stickyPx: [...document.querySelectorAll('header, nav, #main *')].filter(e => ['sticky', 'fixed'].includes(getComputedStyle(e).position) && e.getBoundingClientRect().height > 0 && e.getBoundingClientRect().top < innerHeight && e.getBoundingClientRect().bottom > 0 && e.getBoundingClientRect().height < innerHeight).reduce((a, e) => a + Math.min(innerHeight, e.getBoundingClientRect().bottom) - Math.max(0, e.getBoundingClientRect().top), 0), vh: innerHeight })));
    }
    await ctx.close();
  }
  // ---- other roles: nurse queue and intake entry (read-only)
  if (run('nurse')) {
    const { ctx, page } = await signIn(browser, { w: 1024, h: 768, touch: true, user: /S\. Thomas RN/ });
    await step('nurse', async () => { await go(page, '/nurse'); await shot(page, 'nurse', '1024-queue'); await shot(page, 'nurse', '1024-queue-full', true); note('nurse links', await page.evaluate(() => [...document.querySelectorAll('#main a[href], #main button')].map(a => (a.getAttribute('href') || 'button') + ' | ' + a.textContent.trim().slice(0, 30)).slice(0, 12))); });
    await ctx.close();
  }
} finally { await browser.close(); }
writeFileSync(`${root}/evidence/reviewer-b-states${only ? '-' + only.join('-') : ''}.json`, JSON.stringify(notes, null, 1));
