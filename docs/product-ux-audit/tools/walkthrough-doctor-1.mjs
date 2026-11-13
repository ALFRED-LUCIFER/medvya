// Reviewer A: doctor, part 1: Today -> next patient -> start consultation -> context -> note -> autosave -> reload.
import { readFileSync, writeFileSync } from 'node:fs';
import { chromium, session, users, tablet, desktop, layout, root } from './walkthrough-lib.mjs';
const st = JSON.parse(readFileSync(`${root}/reviews/reviewer-a-evidence/walkthrough-a-state.json`, 'utf8'));
const name = st.patient.name;
const flat = t => t.replace(/\n+/g, ' | ');
const browser = await chromium.launch();
const s = await session(browser, users.doctor, 'doctor', 'consult');
const { page } = s;
try {
  s.log('=== doctor: landing ' + page.url());
  await s.goto('/today'); s.reset(); await s.shot('today'); await s.shot('today-full', true);
  s.log('  today: ' + flat(await s.text()).slice(0, 2600));
  const next = page.getByRole('region', { name: 'Next patient' });
  s.log('  NEXT PATIENT region: ' + flat(await next.innerText().catch(() => 'none')));
  const visit = page.getByRole('region', { name: "Today's visits" }).getByRole('article', { name: new RegExp(name) });
  s.log('  our visit: ' + flat(await visit.innerText()));
  s.log('  position of our visit in the list: ' + await page.getByRole('region', { name: "Today's visits" }).getByRole('article').evaluateAll((a, n) => a.findIndex(x => x.textContent.includes(n)) + 1 + ' of ' + a.length, name));
  s.log('  order of visits: ' + (await page.getByRole('region', { name: "Today's visits" }).getByRole('article').evaluateAll(a => a.map(x => x.textContent.slice(0, 34)))).join(' ; '));
  await s.click(visit.getByRole('button', { name: /Start consultation/ }), 'Start consultation');
  s.log('  after start: url=' + page.url().replace(/[0-9a-f-]{36}/, ':id') + ' dialogs=' + await page.getByRole('dialog').count());
  st.consultPath = new URL(page.url()).pathname.replace('/app', ''); writeFileSync(`${root}/reviews/reviewer-a-evidence/walkthrough-a-state.json`, JSON.stringify(st, null, 1));
  await s.shot('consult-top'); await s.shot('consult-full', true);
  const banner = page.getByRole('region', { name: 'Patient identity' });
  s.log('  banner: ' + flat(await banner.innerText()));
  s.log('  consult text: ' + flat(await s.text()).slice(0, 5000));
  const sel = { header: 'header', banner: '[aria-label="Patient identity"]', noteRegion: '[aria-label="Consultation note"]', sectionNav: '[aria-label="Note sections"]', firstTextarea: '#main textarea', firstInput: '#main input:not([type=hidden])' };
  s.log('  layout 1440x900: ' + JSON.stringify(await layout(page, sel)) + ' docH=' + await page.evaluate(() => document.documentElement.scrollHeight));
  s.log('  banner sticky? ' + await banner.evaluate(e => { let n = e; while (n && n !== document.body) { const p = getComputedStyle(n).position; if (p === 'sticky' || p === 'fixed') return p + ' top=' + getComputedStyle(n).top; n = n.parentElement; } return 'static'; }));
  await page.evaluate(() => scrollTo(0, 1600)); await page.waitForTimeout(300); await s.shot('consult-scrolled-1600');
  s.log('  banner visible in viewport after scrolling 1600px? ' + await banner.evaluate(e => { const r = e.getBoundingClientRect(); return r.bottom > 0 && r.top < innerHeight; }));
  s.log('  anything naming the patient or allergy in viewport at 1600px? ' + await page.evaluate(n => { const out = []; for (const e of document.querySelectorAll('#main *, header *')) { if (e.children.length) continue; const t = e.textContent || ''; const r = e.getBoundingClientRect(); if (r.bottom > 0 && r.top < innerHeight && r.height > 0 && (t.includes(n.split(' ')[0]) || /Penicillin|Allerg/i.test(t))) out.push(t.slice(0, 60)); } return out.join(' ; ') || 'NOTHING'; }, name));
  await page.evaluate(() => scrollTo(0, 0));
  // tools panel: Chart and Checklist
  const tools = page.getByRole('complementary', { name: 'Consultation tools' });
  for (const tab of ['Chart', 'Checklist', 'Assist']) { const t = tools.getByRole('tab', { name: new RegExp('^' + tab) }); if (await t.count()) { await s.click(t, 'tools tab ' + tab); s.log(`  tools/${tab}: ` + flat(await tools.innerText()).slice(0, 1200)); await s.shot('tools-' + tab.toLowerCase()); } }
  // the note
  const note = page.getByRole('region', { name: 'Consultation note' });
  s.log('  textboxes: ' + (await note.getByRole('textbox').evaluateAll(a => a.map(x => (x.getAttribute('aria-label') || document.querySelector(`label[for="${x.id}"]`)?.textContent || x.id) + '=' + JSON.stringify(x.value.slice(0, 40))))).join(' ; '));
  const t0 = Date.now();
  await s.fill(note.getByRole('textbox', { name: /^Chief complaint/ }), 'Fever and sore throat for three days (synthetic)', 'Chief complaint');
  const saved = note.getByRole('status').filter({ hasText: /^Saved/ });
  await saved.first().waitFor({ timeout: 15000 }).then(() => s.log(`  autosave: "Saved" shown ${Date.now() - t0} ms after typing (script-measured)`)).catch(() => s.log('  autosave: no "Saved" status within 15 s'));
  s.log('  save status texts: ' + (await note.getByRole('status').allInnerTexts()).join(' / '));
  await s.fill(note.getByRole('textbox', { name: /^Subjective/ }), 'No cough. Pain on swallowing. (synthetic)', 'Subjective');
  await s.fill(note.getByRole('textbox', { name: /^Objective/ }), 'Throat congested, tonsils enlarged with exudate. Chest clear. (synthetic)', 'Objective');
  await s.fill(note.getByRole('textbox', { name: /^Assessment/ }), 'Acute bacterial tonsillitis (synthetic)', 'Assessment');
  await s.fill(note.getByRole('textbox', { name: /^Plan/ }), 'Antibiotic, paracetamol, fluids. Review in 5 days. (synthetic)', 'Plan');
  await page.waitForTimeout(2500); await s.shot('note-typed', true);
  s.log('  status after typing all: ' + (await note.getByRole('status').allInnerTexts()).join(' / '));
  // type and reload immediately: is the last second of typing lost?
  await note.getByRole('textbox', { name: /^Plan/ }).fill('Antibiotic, paracetamol, fluids. Review in 5 days. SAFETY-NET ADVICE GIVEN (synthetic)');
  await page.reload(); await s.settle();
  s.log('  reload straight after typing -> Plan is: ' + JSON.stringify(await page.getByRole('region', { name: 'Consultation note' }).getByRole('textbox', { name: /^Plan/ }).inputValue()));
  await page.getByRole('region', { name: 'Consultation note' }).getByRole('textbox', { name: /^Plan/ }).fill('Antibiotic, paracetamol, fluids. Review in 5 days. Safety-net advice given. (synthetic)');
  await page.waitForTimeout(3000);
  s.counts('doctor: Today -> start -> context -> note typed (5 fields)');
  s.log('  buttons in main: ' + (await page.locator('#main button, #main a[href]').allInnerTexts()).map(x => x.replace(/\n/g, ' ')).join(' / ').slice(0, 1800));
} catch (e) { s.log('FAILED doctor-1: ' + String(e).slice(0, 500)); await s.shot('failure'); s.log('  text: ' + flat(await s.text()).slice(0, 2000)); }
await s.close();

// same consultation on a tablet: how much room is left for the note
if (st.consultPath) {
  const t = await session(browser, users.doctor, 'doctor', 'consult-tablet', tablet);
  try {
    await t.goto(st.consultPath); await t.shot('consult-1024'); await t.shot('consult-1024-full', true);
    t.log('  layout 1024x768: ' + JSON.stringify(await layout(t.page, { header: 'header', banner: '[aria-label="Patient identity"]', sectionNav: '[aria-label="Note sections"]', firstTextarea: '#main textarea', tools: '[aria-label="Consultation tools"]' })) + ' docH=' + await t.page.evaluate(() => document.documentElement.scrollHeight));
    await t.page.getByRole('button', { name: 'Guided steps' }).click(); await t.settle(); await t.shot('guided-steps-1024'); 
    t.log('  guided steps text: ' + flat(await t.text()).slice(0, 1500) + ' docH=' + await t.page.evaluate(() => document.documentElement.scrollHeight));
    await t.page.getByRole('button', { name: 'One page' }).click(); await t.settle();
  } catch (e) { t.log('FAILED tablet: ' + String(e).slice(0, 300)); }
  await t.close();
}
await browser.close();
