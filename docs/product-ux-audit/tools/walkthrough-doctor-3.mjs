// Reviewer A: doctor, part 3: board state during consult, problems, free-text allergy bypass test, override, authorise, orders, follow-up, sign, print, end, addendum.
import { readFileSync } from 'node:fs';
import { chromium, session, users, root } from './walkthrough-lib.mjs';
const st = JSON.parse(readFileSync(`${root}/reviews/reviewer-a-evidence/walkthrough-a-state.json`, 'utf8'));
const name = st.patient.name;
const flat = t => t.replace(/\n+/g, ' | ');
const only = process.argv[2]?.split(',');
const browser = await chromium.launch();
const s = await session(browser, users.doctor, 'doctor', 'consult-sign');
const { page } = s;
const step = async (n, fn) => { if (only && !only.includes(n)) return; s.log('--- ' + n); try { await fn(); } catch (e) { s.log(`FAILED ${n}: ${String(e).slice(0, 300)}`); await s.shot('failure-' + n).catch(() => {}); s.log('  text: ' + flat(await s.text()).slice(0, 1200)); } };
const stepUp = async () => { await page.getByRole('link', { name: users.doctor }).click(); s.c.clicks++; await page.waitForURL(/\/app\/consult\//); await s.settle(); };
const note = page.getByRole('region', { name: 'Consultation note' });
const rx = page.getByRole('region', { name: 'Prescription' });

await step('board-during-consult', async () => {
  const d = await session(browser, users.receptionist, 'handoff', 'during-consult');
  await d.goto('/front-desk'); const card = d.page.getByRole('article', { name });
  d.log('  desk sees: ' + flat(await card.innerText()) + ' || column=' + await card.evaluate(e => e.closest('[role=region],section')?.getAttribute('aria-label') ?? e.parentElement?.parentElement?.textContent?.slice(0, 20)) + ' || actions: ' + (await card.getByRole('button').allInnerTexts()).join(' / '));
  await d.shot('desk-sees-in-consult'); await d.close();
});
await s.goto(st.consultPath); s.reset();
await step('problems', async () => {
  await s.click(page.getByRole('link', { name: 'Problems and diagnoses' }).or(page.getByRole('button', { name: 'Problems and diagnoses' })).first(), 'Problems and diagnoses');
  const box = page.getByLabel('Search ICD-10 by code or words');
  await s.fill(box, 'tonsillitis', 'ICD search'); await page.waitForTimeout(1500); await s.shot('icd-search');
  s.log('  results: ' + flat(await s.text()).slice(flat(await s.text()).indexOf('Add a problem')).slice(0, 700));
  let hit = page.getByRole('option').or(page.locator('#main li button')).first();
  if (!(await hit.count())) { await box.fill('fever'); await page.waitForTimeout(1500); s.log('  results fever: ' + flat(await s.text()).slice(0, 900)); hit = page.getByRole('option').or(page.locator('#main li button')).first(); }
  if (await hit.count()) { s.log('  picking: ' + (await hit.innerText()).replace(/\n/g, ' ')); await s.click(hit, 'pick code'); }
  else await s.fill(page.getByLabel('Description'), 'Acute tonsillitis (synthetic)', 'description');
  await s.click(page.getByRole('button', { name: 'Add problem' }), 'Add problem'); await s.shot('problem-added');
  s.log('  after add: ' + flat(await s.text()).slice(150, 1100));
  await s.click(page.getByRole('link', { name: 'Back to the consultation' }).or(page.getByRole('button', { name: 'Back to the consultation' })).first(), 'Back to the consultation');
  s.counts('doctor: add one diagnosis (round trip)');
});
await step('free-text-bypass', async () => {
  s.reset();
  await s.fill(rx.getByLabel('Medicine', { exact: true }), 'Augmentin 625 Duo', 'free text brand of amoxicillin+clavulanate');
  await page.waitForTimeout(1200);
  s.log('  matches for a brand name: ' + (await rx.getByRole('list', { name: 'Formulary matches' }).getByRole('button').allInnerTexts().catch(() => [])).join(' ; '));
  await s.click(rx.getByRole('button', { name: 'Add as free text' }), 'Add as free text'); await page.waitForTimeout(1500); await rx.scrollIntoViewIfNeeded(); await s.shot('free-text-augmentin');
  s.log('  rx with free-text Augmentin: ' + flat(await rx.innerText()).slice(0, 3200));
});
await step('authorise-with-severe', async () => {
  await s.click(rx.getByRole('button', { name: 'Authorise prescription' }), 'Authorise with unresolved severe alert'); await s.shot('authorise-refused');
  s.log('  alerts: ' + (await rx.getByRole('alert').allInnerTexts()).join(' / ') + ' status: ' + (await rx.getByRole('status').allInnerTexts()).join(' / '));
  // the override path
  await s.click(rx.getByRole('button', { name: 'Give a reason to go ahead' }).first(), 'Give a reason to go ahead'); await s.shot('override-form');
  s.log('  override form: ' + flat(await rx.getByRole('region', { name: 'Safety checks' }).innerText().catch(async () => await rx.innerText())).slice(0, 900));
  // do not override: remove the amoxicillin line instead
  const cancel = rx.getByRole('button', { name: /^Cancel$|Close/ }).first(); if (await cancel.count()) await cancel.click();
  const amoxRow = rx.getByRole('row', { name: /^Amoxicillin/ });
  await s.click(amoxRow.getByRole('button', { name: /Remove/ }).first(), 'Remove amoxicillin'); await page.waitForTimeout(1000);
  s.log('  rows now: ' + (await rx.getByRole('row').evaluateAll(r => r.map(x => (x.getAttribute('aria-label') || x.textContent || '').slice(0, 30)))).join(' ; '));
});
await step('authorise', async () => {
  s.reset();
  s.log('  rx before authorise: ' + flat(await rx.innerText()).slice(0, 1800));
  // free-text line needs dose etc.? try authorising as is
  await s.click(rx.getByRole('button', { name: 'Authorise prescription' }), 'Authorise prescription'); await s.shot('authorise-first-click');
  s.log('  after first click: alerts=' + (await rx.getByRole('alert').allInnerTexts()).join(' / '));
  const confirm = rx.getByRole('button', { name: 'Confirm identity' });
  if (await confirm.count()) { await s.click(confirm, 'Confirm identity'); await s.shot('step-up-provider'); await stepUp(); await s.click(rx.getByRole('button', { name: 'Authorise prescription' }), 'Authorise prescription (again)'); }
  await rx.scrollIntoViewIfNeeded(); await s.shot('authorised');
  s.log('  status: ' + (await rx.getByRole('status').allInnerTexts()).join(' / ') + ' alerts: ' + (await rx.getByRole('alert').allInnerTexts()).join(' / '));
  s.log('  rx after: ' + flat(await rx.innerText()).slice(0, 1500));
  s.counts('doctor: authorise prescription incl. step-up');
});
await step('orders-followup', async () => {
  s.reset();
  const ordersHead = page.getByRole('button', { name: /^Orders/ }).first(); if (await ordersHead.count() && (await ordersHead.getAttribute('aria-expanded')) === 'false') await s.click(ordersHead, 'open Orders');
  const orders = page.getByRole('region', { name: 'Orders' });
  s.log('  orders: ' + flat(await orders.innerText()).slice(0, 900));
  await s.fill(orders.getByLabel('Test', { exact: true }), 'CBC', 'Test'); await s.click(orders.getByRole('button', { name: 'Add test' }), 'Add test');
  await s.click(orders.getByRole('button', { name: 'Place orders' }), 'Place orders'); await s.shot('orders-placed');
  s.log('  orders after: ' + flat(await orders.innerText()).slice(0, 700));
  await s.click(note.getByRole('button', { name: '2 weeks' }), 'Follow-up 2 weeks'); await page.waitForTimeout(1200); await s.shot('follow-up');
  const fu = flat(await note.innerText()); s.log('  follow-up area: ' + fu.slice(fu.indexOf('Follow-up'), fu.indexOf('Follow-up') + 500));
  s.counts('doctor: one test ordered + follow-up set');
});
await step('sign', async () => {
  s.reset();
  const sign = note.getByRole('button', { name: 'Sign note' });
  s.log('  sign enabled=' + await sign.isEnabled() + ' desc=' + await sign.evaluate(e => document.getElementById(e.getAttribute('aria-describedby') || '')?.textContent));
  await s.click(sign, 'Sign note'); await s.shot('sign-dialog');
  const dlg = page.getByRole('dialog'); s.log('  dialog: ' + flat(await dlg.innerText()).slice(0, 1500));
  await s.click(dlg.getByRole('button', { name: /^Sign as/ }), 'Sign as …'); await s.shot('after-sign-click');
  if (/dev-idp|authorize/.test(page.url()) || await page.getByRole('link', { name: users.doctor }).count()) { await stepUp(); s.log('  step-up needed again for signing (after the prescription step-up)'); await s.click(sign, 'Sign note (again)'); await s.click(page.getByRole('dialog').getByRole('button', { name: /^Sign as/ }), 'Sign as … (again)'); }
  await page.waitForTimeout(1500); await s.shot('signed'); await s.shot('signed-full', true);
  const t = flat(await note.innerText()); s.log('  signed note: ' + t.slice(0, 2500));
  s.counts('doctor: sign note');
});
await step('prints', async () => {
  s.reset();
  s.log('  links after signing: ' + (await page.locator('#main a[href]').evaluateAll(a => a.map(x => x.textContent.trim() + ' -> ' + x.getAttribute('href').replace(/[0-9a-f-]{36}/g, ':id')))).join(' ; '));
  await s.goto(st.consultPath + '/rx/print'); await s.shot('rx-print', true); s.log('  rx print: ' + flat(await s.text()).slice(0, 1800));
  await s.goto(st.consultPath + '/note/print'); await s.shot('note-print', true); s.log('  note print: ' + flat(await s.text()).slice(0, 2000));
  await s.goto(st.consultPath + '/slip'); await s.shot('slip', true); s.log('  slip: ' + flat(await s.text()).slice(0, 800));
});
await step('end-and-addendum', async () => {
  await s.goto(st.consultPath); s.reset();
  await s.click(page.getByRole('button', { name: 'End consultation' }).first(), 'End consultation'); await s.shot('ended');
  s.log('  dialogs after End: ' + await page.getByRole('dialog').count() + ' status: ' + (await page.getByRole('status').allInnerTexts()).join(' / ').slice(0, 300));
  s.log('  buttons after end: ' + (await page.locator('#main button').allInnerTexts()).filter(x => x.trim()).join(' / ').slice(0, 700));
  const add = page.getByRole('button', { name: 'Add addendum' });
  if (await add.count()) { await s.click(add, 'Add addendum'); await s.fill(page.getByLabel('Addendum', { exact: true }), 'Patient phoned: rash after first dose, medicine stopped (synthetic)', 'addendum'); await s.fill(page.getByLabel('Reason for the addendum'), 'New information (synthetic)', 'reason'); await s.shot('addendum-form');
    s.log('  addendum form: ' + flat(await note.innerText()).slice(-900));
    await s.click(page.getByRole('button', { name: 'Sign addendum' }), 'Sign addendum'); await page.waitForTimeout(1200); await s.shot('addendum-after-sign');
    s.log('  after sign addendum: url=' + page.url().replace(/[0-9a-f-]{36}/g, ':id') + ' text: ' + flat(await note.innerText().catch(() => '')).slice(-700)); }
  else s.log('  no Add addendum button');
  s.counts('doctor: end consultation + addendum');
  await s.goto('/today'); await s.shot('today-after', true);
  const v = page.getByRole('region', { name: "Today's visits" }).getByRole('article', { name: new RegExp(name) });
  s.log('  Today row after: ' + flat(await v.innerText()));
  s.log('  notes to sign: ' + flat(await page.getByRole('region', { name: /notes? to sign$/ }).innerText().catch(() => 'none')));
  await s.goto('/front-desk'); const card = page.getByRole('article', { name });
  s.log('  board card after End: ' + flat(await card.innerText()) + ' actions: ' + (await card.getByRole('button').allInnerTexts()).join(' / '));
  await s.shot('board-after-end');
});
await browser.close();
