// Reviewer A: read-only cross-role checks after the walkthroughs.
import { readFileSync } from 'node:fs';
import { chromium, session, users, root, tablet } from './walkthrough-lib.mjs';
const st = JSON.parse(readFileSync(`${root}/reviews/reviewer-a-evidence/walkthrough-a-state.json`, 'utf8'));
const flat = t => t.replace(/\n+/g, ' | ');
const browser = await chromium.launch();
const tryIt = async (s, n, fn) => { s.log('--- ' + n); try { await fn(); } catch (e) { s.log(`FAILED ${n}: ${String(e).slice(0, 250)}`); } };

const d = await session(browser, users.receptionist, 'handoff', 'follow-up-at-desk');
await tryIt(d, 'follow-up visible to the desk?', async () => {
  await d.goto('/front-desk'); const t = await d.text(); d.log('  board mentions follow-up: ' + /follow-up/i.test(t)); await d.shot('board');
  const done = d.page.getByRole('region', { name: /^Done/ }).getByRole('article', { name: /Lalitha Raman/ }); d.log('  done card: ' + flat(await done.innerText()) + ' buttons=' + (await done.getByRole('button').count()) + ' links=' + (await done.getByRole('link').count()));
  await d.goto('/patients/directory'); await d.page.getByRole('searchbox').first().fill('Lalitha'); await d.page.waitForTimeout(1200);
  await d.page.getByRole('link', { name: /Lalitha Raman/ }).first().click(); await d.settle(); await d.shot('patient-record', true);
  const r = flat(await d.text()); d.log('  record mentions follow-up: ' + /follow-up/i.test(r) + ' :: visits part: ' + r.slice(r.indexOf('Visits'), r.indexOf('Visits') + 300));
  await d.goto('/schedule?date=2026-11-17'); d.log('  schedule on the follow-up date mentions the patient: ' + /Lalitha/.test(await d.text()));
  await d.goto('/today'); d.log('  receptionist /today: ' + flat(await d.text()).slice(0, 120));
  await d.goto(st.consultPath); d.log('  receptionist opening a consult URL: ' + flat(await d.text()).slice(0, 160));
  await d.goto('/patients/' + 'x'); 
});
await d.close();

const n = await session(browser, users.nurse, 'nurse', 'flag-off', tablet);
await tryIt(n, 'nurse with nurse intake OFF', async () => {
  n.log('  landing: ' + n.page.url() + ' nav: ' + (await n.page.locator('nav a').evaluateAll(a => [...new Set(a.map(x => x.getAttribute('href')))])).join(' '));
  await n.shot('landing'); const c = n.page.getByRole('article').first(); n.log('  first card actions: ' + (await n.page.locator('#main article button, #main article a').allInnerTexts()).filter((v, i, a) => a.indexOf(v) === i).join(' / '));
  await n.goto(st.intakeUrl.replace(/^.*\/app/, '')); n.log('  intake url with flag off: ' + flat(await n.text()).slice(0, 200));
  const cc = n.page.getByLabel("Chief complaint, in the patient's words"); if (await cc.count()) n.log('  nurse complaint stored: ' + JSON.stringify(await cc.inputValue()));
  await n.goto('/patients/directory'); n.log('  nurse patient directory: ' + flat(await n.text()).slice(0, 200));
});
await n.close();

const doc = await session(browser, users.doctor, 'doctor', 'misc');
await tryIt(doc, 'doctor misc', async () => {
  doc.log('  landing after sign-in: ' + doc.page.url().replace(/[0-9a-f]{8}-[0-9a-f-]{27}/, ':patientId'));
  await doc.shot('landing-after-sign-in');
  doc.log('  landing h1/banner: ' + flat(await doc.text()).slice(0, 200));
  await doc.goto('/today'); doc.reset();
  await doc.click(doc.page.getByRole('button', { name: /Search patients/ }), 'Search patients');
  const dlg = doc.page.getByRole('dialog', { name: 'Search patients' }); await doc.fill(dlg.getByRole('combobox'), '0102', 'phone digits'); await doc.page.waitForTimeout(1200); await doc.shot('global-search');
  doc.log('  global search: ' + flat(await dlg.innerText()).slice(0, 900));
  await doc.page.keyboard.press('Escape');
  // the nurse's complaint in the doctor's (ended) consultation
  await doc.goto(st.consultPath); const t = await doc.text(); doc.log('  consultation shows the nurse complaint text anywhere besides the doctor-typed field: occurrences of "sore throat" = ' + (t.match(/sore throat/g) || []).length);
  // whole clinic toggle + escalation prominence
  await doc.goto('/today'); await doc.click(doc.page.getByRole('button', { name: 'Whole clinic' }), 'Whole clinic'); await doc.shot('today-whole-clinic', true);
  const tt = flat(await doc.text()); doc.log('  whole clinic: ' + tt.slice(0, 700));
  doc.log('  escalation elements: ' + JSON.stringify(await doc.page.getByText(/^Nurse escalation/).evaluateAll(a => a.map(e => { const r = e.getBoundingClientRect(); return { y: Math.round(r.top + scrollY), role: e.closest('[role=alert],[role=status]')?.getAttribute('role') ?? null, color: getComputedStyle(e).color, bg: getComputedStyle(e).backgroundColor }; }))));
  await doc.goto('/patients'); doc.log('  /patients -> ' + doc.page.url() + ' :: ' + flat(await doc.text()).slice(0, 150));
  for (const p of ['/inbox', '/billing', '/messages', '/care', '/referrals', '/devices', '/video']) { await doc.goto(p); doc.log(`  ${p}: ` + flat(await doc.text()).slice(0, 80)); }
});
await doc.close();
await browser.close();
