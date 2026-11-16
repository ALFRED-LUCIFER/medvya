// Reviewer A: doctor, part 2: navigation flush, problems, reconciliation, prescription with an allergy conflict, orders, follow-up.
import { readFileSync } from 'node:fs';
import { chromium, session, users, root } from './walkthrough-lib.mjs';
const st = JSON.parse(readFileSync(`${root}/reviews/reviewer-a-evidence/walkthrough-a-state.json`, 'utf8'));
const flat = t => t.replace(/\n+/g, ' | ');
const browser = await chromium.launch();
const s = await session(browser, users.doctor, 'doctor', 'consult-orders');
const { page } = s;
const step = async (name, fn) => { try { await fn(); } catch (e) { s.log(`FAILED ${name}: ${String(e).slice(0, 300)}`); await s.shot('failure-' + name).catch(() => {}); s.log('  text: ' + flat(await s.text()).slice(0, 1500)); } };
await s.goto(st.consultPath); s.reset();
const note = page.getByRole('region', { name: 'Consultation note' });

await step('flush-on-navigation', async () => {
  const plan = note.getByRole('textbox', { name: /^Plan/ });
  s.log('  Plan now: ' + JSON.stringify(await plan.inputValue()));
  await plan.fill('Antibiotic, paracetamol, fluids. Review in 5 days. NAV-FLUSH-TEST (synthetic)');
  await page.getByRole('link', { name: 'Problems and diagnoses' }).or(page.getByRole('button', { name: 'Problems and diagnoses' })).first().click(); s.c.clicks++;
  await s.settle(); await s.shot('problems');
  s.log('  problems page: ' + flat(await s.text()).slice(0, 1800));
  s.log('  problems: banner present=' + await page.getByRole('region', { name: 'Patient identity' }).count());
});
await step('problems', async () => {
  const box = page.getByRole('searchbox').or(page.getByRole('combobox')).or(page.getByRole('textbox')).first();
  s.log('  first input: ' + await box.evaluate(e => e.getAttribute('aria-label') || document.querySelector(`label[for="${e.id}"]`)?.textContent || e.placeholder));
  await s.fill(box, 'tonsillitis', 'diagnosis search'); await page.waitForTimeout(1500); await s.shot('problems-search');
  s.log('  after search: ' + flat(await s.text()).slice(0, 1500));
  const hit = page.getByRole('button', { name: /tonsillitis/i }).or(page.getByRole('option', { name: /tonsillitis/i })).first();
  if (await hit.count()) { await s.click(hit, 'pick diagnosis'); await s.shot('problems-picked'); s.log('  after pick: ' + flat(await s.text()).slice(0, 1500)); }
  else { await box.fill('pharyngitis'); await page.waitForTimeout(1500); s.log('  search pharyngitis: ' + flat(await s.text()).slice(0, 1200)); }
});
await step('back-to-consult', async () => {
  const back = page.getByRole('link', { name: /Back to the consultation/ }).or(page.getByRole('button', { name: /Back to the consultation/ })).first();
  s.log('  back control: ' + (await back.count() ? await back.innerText() : 'NONE; links: ' + (await page.locator('#main a').allInnerTexts()).join(' / ')));
  if (await back.count()) await s.click(back, 'Back to the consultation'); else await s.goto(st.consultPath);
  s.log('  Plan after leaving within the autosave delay: ' + JSON.stringify(await note.getByRole('textbox', { name: /^Plan/ }).inputValue()));
  await note.getByRole('textbox', { name: /^Plan/ }).fill('Antibiotic, paracetamol, fluids. Review in 5 days. Safety-net advice given. (synthetic)'); await page.waitForTimeout(2500);
});
await step('reconcile', async () => {
  await s.click(page.getByRole('link', { name: 'Reconcile medicines' }).or(page.getByRole('button', { name: 'Reconcile medicines' })).first(), 'Reconcile medicines'); await s.shot('reconcile');
  s.log('  reconcile page: ' + flat(await s.text()).slice(0, 1200));
  await s.goto(st.consultPath);
});
await step('add-medicine-dialog', async () => {
  await s.click(note.getByRole('button', { name: 'Add medicine' }).first(), 'Add medicine (Plan)');
  const panel = page.getByRole('dialog', { name: 'Add medicine' });
  await s.shot('add-medicine-dialog'); s.log('  dialog: ' + flat(await panel.innerText()).slice(0, 1500));
  await s.fill(panel.getByLabel('Search medicine'), 'amox', 'search amox'); await page.waitForTimeout(1200);
  s.log('  matches: ' + (await panel.getByRole('button', { name: /^Amox/ }).allInnerTexts()).map(x => x.replace(/\n/g, ' ')).join(' ; '));
  await s.click(panel.getByRole('button', { name: /^Amoxicillin/ }).first(), 'pick Amoxicillin'); await s.shot('amoxicillin-picked');
  s.log('  dialog after pick (before dose): ' + flat(await panel.innerText()).slice(0, 2200));
  const choose = async (g, o) => s.click(panel.getByRole('group', { name: g }).getByRole('button', { name: o, exact: true }), `${g}: ${o}`);
  await choose('How often', '1-1-1 · three times a day'); await choose('When', 'After food'); await choose('For how long', '5 days');
  await s.shot('amoxicillin-blocked'); s.log('  dialog after dosing: ' + flat(await panel.innerText()).slice(0, 2600));
  s.log('  Add to prescription enabled=' + await panel.getByRole('button', { name: 'Add to prescription' }).isEnabled());
  s.log('  override controls: ' + (await panel.getByRole('button').allInnerTexts()).filter(x => /override|reason|anyway|continue/i.test(x)).join(' / ') + ' textboxes: ' + (await panel.getByRole('textbox').evaluateAll(a => a.map(x => x.getAttribute('aria-label') || document.querySelector(`label[for="${x.id}"]`)?.textContent))).join(' / '));
  await s.click(panel.getByRole('button', { name: 'Cancel' }), 'Cancel');
  s.counts('doctor: attempt amoxicillin for a penicillin-allergic patient');
  s.reset();
  await s.click(note.getByRole('button', { name: 'Add medicine' }).first(), 'Add medicine (Plan)');
  await s.fill(panel.getByLabel('Search medicine'), 'azith', 'search azith'); await page.waitForTimeout(1200);
  s.log('  matches azith: ' + (await panel.getByRole('button').allInnerTexts()).filter(x => /azith/i.test(x)).map(x => x.replace(/\n/g, ' ')).join(' ; '));
  const az = panel.getByRole('button', { name: /^Azithromycin/ }).first();
  if (await az.count()) { await s.click(az, 'pick Azithromycin'); await choose('How often', '1-0-0 · once a day').catch(async () => { s.log('  how-often options: ' + (await panel.getByRole('group', { name: 'How often' }).getByRole('button').allInnerTexts()).join(' / ')); await panel.getByRole('group', { name: 'How often' }).getByRole('button').first().click(); s.c.clicks++; });
    await choose('When', 'After food'); await choose('For how long', '3 days').catch(async () => { s.log('  duration options: ' + (await panel.getByRole('group', { name: 'For how long' }).getByRole('button').allInnerTexts()).join(' / ')); await panel.getByRole('group', { name: 'For how long' }).getByRole('button').first().click(); s.c.clicks++; }); }
  else { await panel.getByLabel('Search medicine').fill('paracetamol'); await page.waitForTimeout(1200); await s.click(panel.getByRole('button', { name: /^Paracetamol/ }).first(), 'pick Paracetamol'); await choose('How often', '1-1-1 · three times a day'); await choose('When', 'After food'); await choose('For how long', '5 days'); }
  await s.shot('medicine-ready'); s.log('  dialog ready: ' + flat(await panel.innerText()).slice(0, 1800));
  await s.click(panel.getByRole('button', { name: 'Add to prescription' }), 'Add to prescription');
  s.counts('doctor: add one medicine through the dialog');
});
await step('prescription-section', async () => {
  const rx = page.getByRole('region', { name: 'Prescription' });
  await rx.scrollIntoViewIfNeeded(); await s.shot('prescription-section');
  s.log('  rx section: ' + flat(await rx.innerText()).slice(0, 2500));
  s.log('  rx top offset px: ' + await rx.evaluate(e => Math.round(e.getBoundingClientRect().top + scrollY)) + ' banner visible now: ' + await page.getByRole('region', { name: 'Patient identity' }).evaluate(e => { const r = e.getBoundingClientRect(); return r.bottom > 0 && r.top < innerHeight; }));
  // the inline table path: amoxicillin typed directly into the table
  await s.fill(rx.getByLabel('Medicine', { exact: true }), 'Amox', 'Medicine (inline)'); await page.waitForTimeout(1200);
  const m = rx.getByRole('list', { name: 'Formulary matches' }).getByRole('button', { name: /^Add Amoxicillin/ }).first();
  s.log('  inline matches: ' + (await rx.getByRole('list', { name: 'Formulary matches' }).getByRole('button').allInnerTexts()).map(x => x.replace(/\n/g, ' ')).join(' ; '));
  await s.click(m, 'Add Amoxicillin inline'); await page.waitForTimeout(1500); await rx.scrollIntoViewIfNeeded(); await s.shot('inline-amoxicillin');
  s.log('  rx after inline amoxicillin: ' + flat(await rx.innerText()).slice(0, 3000));
  const auth = rx.getByRole('button', { name: 'Authorise prescription' });
  s.log('  Authorise enabled with amoxicillin on the draft? ' + await auth.isEnabled().catch(() => 'no button'));
});
await browser.close();
