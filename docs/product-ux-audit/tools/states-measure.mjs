// Reviewer B (visual and interaction): read-only layout, type and contrast measurements on the throwaway local stack.
// Run: node docs/product-ux-audit/tools/states-measure.mjs   → docs/product-ux-audit/evidence/reviewer-b-measure.jsonl
// Signs in as the synthetic doctor (and nurse for the intake queue), opens each key route at 1440x900, 1024x768 and 375x812. Clicks nothing that writes.
import { createRequire } from 'node:module';
import { writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(here, '..');
const require = createRequire(resolve(here, '../../../tests/e2e/package.json'));
const { chromium } = require('@playwright/test');
const origin = 'http://localhost:5391';
const now = new Date(Date.parse('2026-11-03T04:00:00Z') + 30 * 60_000);
const P = '5e1d0001-0004-4000-8000-000000000001', C = '01a1d400-8c17-7d5e-94c3-40c10b20ce3e';

// slug, path, how to find the first piece of working content (CSS selector, or text:<regex> for the first element whose own text matches)
const routes = {
  doctor: [
    ['today', '/today', 'text:^Next patient$'],
    ['front-desk', '/front-desk', 'article'],
    ['front-desk-overview', '/front-desk/overview', 'text:^(Your day|Doctors today)'],
    ['schedule', '/schedule', 'text:^09:00$'],
    ['walk-in', '/front-desk/walk-in', 'input'],
    ['register-patient', '/patients/new', 'input'],
    ['register-quick', '/patients/new?mode=quick', 'input'],
    ['patients-directory', '/patients/directory', 'tbody tr'],
    ['patient-360', `/patients/${P}`, 'text:^Last vitals$'],
    ['patient-record', `/patients/directory/${P}`, 'text:^Identity$'],
    ['consult', `/consult/${C}`, 'textarea'],
    ['settings-profile', '/settings?tab=profile', 'input'],
    ['settings-hours', '/settings?tab=hours', '[role=switch]'],
    ['settings-staff', '/settings?tab=staff', 'tbody tr'],
    ['settings-medicines', '/settings?tab=medicines', 'tbody tr'],
  ],
  nurse: [['nurse-queue', '/nurse', 'article, tbody tr, li']],
};
const users = { doctor: /Dr\. Arjun Rao/, nurse: /S\. Thomas RN/ };
const sizes = [[1440, 900, false], [1024, 768, true], [375, 812, true]];

async function signIn(context, user) {
  const page = await context.newPage();
  await page.addInitScript(() => localStorage.setItem('medvya.locale', 'en'));
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

const measure = (page, first) => page.evaluate(first => {
  const vis = e => { const r = e.getBoundingClientRect(); const s = getComputedStyle(e); return r.width > 0 && r.height > 0 && s.visibility !== 'hidden' && s.display !== 'none'; };
  const box = e => e.getBoundingClientRect();
  const main = document.getElementById('main');
  const own = e => [...e.childNodes].filter(n => n.nodeType === 3).map(n => n.textContent).join('').trim();
  const label = e => (e.getAttribute('aria-label') || e.textContent || e.tagName).trim().replace(/\s+/g, ' ').slice(0, 40);
  const hist = (list, f) => { const o = {}; for (const e of list) { const k = f(e); o[k] = (o[k] || 0) + 1; } return Object.fromEntries(Object.entries(o).sort((a, b) => b[1] - a[1])); };
  const textEls = [...document.querySelectorAll('#main *, header *, nav *')].filter(e => own(e) && vis(e));
  const inMain = textEls.filter(e => main.contains(e));

  // first working content
  let firstEl = null;
  if (first.startsWith('text:')) { const re = new RegExp(first.slice(5)); firstEl = inMain.find(e => re.test(own(e))); }
  else firstEl = [...main.querySelectorAll(first)].find(vis);
  const firstTop = firstEl ? Math.round(box(firstEl).top + scrollY) : null;

  // buttons
  const btns = [...main.querySelectorAll('[data-slot=button], button, a[class*="min-h-"]')].filter(vis);
  const primary = btns.filter(b => b.dataset.variant === 'default' || /\bbg-primary\b/.test(b.className));
  const lines = e => { const r = document.createRange(); r.selectNodeContents(e); return new Set([...r.getClientRects()].filter(x => x.width > 1).map(x => Math.round(x.top))).size; };
  const wrapped = [...main.querySelectorAll('[data-slot=button], button, h3, th')].filter(vis).filter(e => own(e) && lines(e) > 1).map(e => `${e.tagName.toLowerCase()}:${label(e)}`);
  const clipped = [...document.querySelectorAll('#main *, header *')].filter(vis).filter(e => own(e) && e.scrollWidth > e.clientWidth + 1 && ['hidden', 'clip'].includes(getComputedStyle(e).overflowX)).map(label);
  // text spilling out of its card
  const spill = []; for (const a of main.querySelectorAll('article')) for (const e of a.querySelectorAll('*')) if (own(e) && vis(e) && box(e).right > box(a).right + 1) spill.push(`${label(e)} +${Math.round(box(e).right - box(a).right)}px`);
  // inner horizontal scrollers
  const scrollers = [...main.querySelectorAll('*')].filter(e => vis(e) && e.scrollWidth > e.clientWidth + 4 && ['auto', 'scroll'].includes(getComputedStyle(e).overflowX)).map(e => `${e.tagName.toLowerCase()}[${(e.getAttribute('aria-label') || e.className.toString().slice(0, 30))}] ${e.clientWidth}/${e.scrollWidth}`);

  // cards
  const cards = [...main.querySelectorAll('section, article, div, aside, form, li')].filter(e => vis(e) && /rounded-(xl|lg)/.test(e.className) && /\bborder\b/.test(e.className) && box(e).height > 60);
  // contrast
  const rgb = s => { const m = s.match(/rgba?\(([^)]+)\)/); if (!m) return null; const p = m[1].split(/[, /]+/).map(Number); return { r: p[0], g: p[1], b: p[2], a: p[3] ?? 1 }; };
  const over = (f, b) => ({ r: f.r * f.a + b.r * (1 - f.a), g: f.g * f.a + b.g * (1 - f.a), b: f.b * f.a + b.b * (1 - f.a), a: 1 });
  const bgOf = e => { const stack = []; for (let n = e; n; n = n.parentElement) { const c = rgb(getComputedStyle(n).backgroundColor); if (c && c.a > 0) { stack.push(c); if (c.a === 1) break; } } let b = { r: 255, g: 255, b: 255, a: 1 }; for (const c of stack.reverse()) b = over(c, b); return b; };
  const lum = c => { const f = v => { v /= 255; return v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4; }; return 0.2126 * f(c.r) + 0.7152 * f(c.g) + 0.0722 * f(c.b); };
  const ratio = (a, b) => { const [x, y] = [lum(a), lum(b)].sort((p, q) => q - p); return (x + 0.05) / (y + 0.05); };
  const hex = c => '#' + [c.r, c.g, c.b].map(v => Math.round(v).toString(16).padStart(2, '0')).join('');
  const pairs = {};
  for (const e of textEls) {
    const s = getComputedStyle(e); const fg0 = rgb(s.color); if (!fg0) continue; const bg = bgOf(e); const fg = over(fg0, bg); const r = ratio(fg, bg);
    const px = parseFloat(s.fontSize), large = px >= 24 || (px >= 18.66 && +s.fontWeight >= 700);
    const k = `${hex(fg)} on ${hex(bg)}`;
    (pairs[k] ??= { ratio: +r.toFixed(2), n: 0, minPx: px, large, sample: label(e), disabled: !!e.closest(':disabled,[aria-disabled=true]') }).n++;
    pairs[k].minPx = Math.min(pairs[k].minPx, px); pairs[k].large &&= large;
  }
  const placeholders = [...main.querySelectorAll('input[placeholder], textarea[placeholder]')].filter(vis).slice(0, 1).map(e => { const c = rgb(getComputedStyle(e, '::placeholder').color), b = bgOf(e); return `${hex(over(c, b))} on ${hex(b)} = ${ratio(over(c, b), b).toFixed(2)}`; });

  const h1 = document.querySelector('h1'); const ms = getComputedStyle(main);
  const header = document.querySelector('header');
  const sticky = [...document.querySelectorAll('#main *')].filter(e => vis(e) && ['sticky', 'fixed'].includes(getComputedStyle(e).position)).map(e => `${getComputedStyle(e).position}:${label(e).slice(0, 24)} top=${Math.round(box(e).top)} h=${Math.round(box(e).height)}`);
  const bottomNav = [...document.querySelectorAll('nav')].find(n => getComputedStyle(n).position === 'fixed' && vis(n));
  const usableBottom = Math.min(innerHeight, bottomNav ? box(bottomNav).top : innerHeight);
  return {
    vw: innerWidth, vh: innerHeight, docH: document.documentElement.scrollHeight, hOverflow: document.documentElement.scrollWidth > innerWidth + 1,
    headerH: header ? Math.round(box(header).height) : null, mainPad: `${ms.paddingTop}/${ms.paddingLeft}`, contentW: Math.round(box(main).width - parseFloat(ms.paddingLeft) * 2),
    h1: h1 ? { text: h1.textContent.trim().slice(0, 50), top: Math.round(box(h1).top), size: getComputedStyle(h1).fontSize, weight: getComputedStyle(h1).fontWeight, lines: lines(h1) } : null,
    firstTop, usefulPct: firstTop == null ? null : Math.max(0, Math.round((usableBottom - Math.min(firstTop, usableBottom)) / innerHeight * 100)), bottomNavH: bottomNav ? Math.round(box(bottomNav).height) : 0,
    fontSizes: hist(inMain, e => getComputedStyle(e).fontSize), fontWeights: hist(inMain, e => getComputedStyle(e).fontWeight),
    sizeWeight: Object.keys(hist(inMain, e => getComputedStyle(e).fontSize + '/' + getComputedStyle(e).fontWeight)).length,
    families: hist(inMain, e => getComputedStyle(e).fontFamily.split(',')[0].replace(/"/g, '')),
    btnHeights: hist(btns, e => Math.round(box(e).height)), primaryTotal: primary.length, primaryInView: primary.filter(b => box(b).top < usableBottom && box(b).bottom > 0).length,
    primaryLabels: [...new Set(primary.map(label))].slice(0, 6),
    cardPad: hist(cards, e => getComputedStyle(e).padding), cardRadius: hist(cards, e => getComputedStyle(e).borderRadius), gaps: hist([...main.querySelectorAll('*')].filter(e => vis(e) && ['flex', 'grid'].includes(getComputedStyle(e).display) && getComputedStyle(e).rowGap !== 'normal'), e => getComputedStyle(e).rowGap),
    widestP: Math.max(0, ...[...main.querySelectorAll('p')].filter(vis).map(p => Math.round(box(p).width / (parseFloat(getComputedStyle(p).fontSize) * 0.5)))),
    wrapped: wrapped.slice(0, 12), wrappedN: wrapped.length, clipped: [...new Set(clipped)].slice(0, 8), spill: spill.slice(0, 8), scrollers: scrollers.slice(0, 6), sticky,
    lowContrast: Object.entries(pairs).filter(([, v]) => v.ratio < (v.large ? 3 : 4.5)).map(([k, v]) => ({ k, ...v })), allPairs: Object.entries(pairs).map(([k, v]) => `${k}=${v.ratio}(${v.n})`), placeholders,
  };
}, first);

const out = [];
const browser = await chromium.launch();
try {
  for (const [role, list] of Object.entries(routes)) for (const [w, h, touch] of sizes) {
    const ctx = await browser.newContext({ viewport: { width: w, height: h }, isMobile: touch, hasTouch: touch, reducedMotion: 'reduce' });
    const page = await signIn(ctx, users[role]).catch(e => { console.log('sign-in failed', role, w, String(e).slice(0, 100)); return null; });
    if (!page) { await ctx.close(); continue; }
    for (const [slug, path, first] of list) {
      await page.goto(origin + '/app' + path, { waitUntil: 'domcontentloaded' }).catch(() => {});
      await page.waitForLoadState('networkidle', { timeout: 10000 }).catch(() => {});
      await page.waitForTimeout(700);
      const m = await measure(page, first).catch(e => ({ error: String(e).slice(0, 200) }));
      out.push({ role, slug, width: w, ...m });
      console.log(role, slug, w, 'first', m.firstTop, 'useful%', m.usefulPct, 'primary', m.primaryInView + '/' + m.primaryTotal, 'wrapped', m.wrappedN, 'spill', (m.spill || []).length);
    }
    await ctx.close();
  }
} finally { await browser.close(); }
writeFileSync(`${root}/evidence/reviewer-b-measure.jsonl`, out.map(o => JSON.stringify(o)).join('\n') + '\n');
