// Reviewer A: doctor, part 4 (second patient): stale text after a quick route change, two tabs on one note, orders, follow-up, authorise + sign step-ups,
// the desk closing the visit mid-consult, a second consultation while one is open, ending with an unauthorised draft prescription.
import { chromium, session, users } from './walkthrough-lib.mjs';
const flat = t => t.replace(/\n+/g, ' | ');
const only = process.argv[2]?.split(',');
const browser = await chromium.launch();
const s = await session(browser, users.doctor, 'doctor', 'consult-2');
const { page } = s;
const step = async (n, fn) => { if (only && !only.includes(n)) return; s.log('--- ' + n); try { await fn(); } catch (e) { s.log(`FAILED ${n}: ${String(e).slice(0, 300)}`); await s.shot('failure-' + n).catch(() => {}); s.log('  text: ' + flat(await s.text()).slice(0, 900)); } };
const stepUp = async () => { await page.getByRole('link', { name: users.doctor }).click(); s.c.clicks++; await page.waitForURL(/\/app\/consult\//); await s.settle(); };
const note = page.getByRole('region', { name: 'Consultation note' });
const rx = page.getByRole('region', { name: 'Prescription' });
const openPart = async title => { const sum = page.locator('summary').filter({ hasText: new RegExp(`^${title}$`) }); if (!(await sum.evaluate(el => el.parentElement.open))) { await sum.click(); s.c.clicks++; } };
let consult = '', consult2 = '';

await step('start', async () => {
  await s.goto('/today'); s.reset();
  const v = page.getByRole('region', { name: "Today's visits" }).getByRole('article', { name: /Lalitha Raman/ });
  await s.click(v.getByRole('button', { name: /Start consultation/ }), 'Start consultation (Lalitha)');
  consult = new URL(page.url()).pathname.replace('/app', ''); s.log('  consult ' + consult.replace(/[0-9a-f-]{36}/, ':id'));
  s.log('  banner: ' + flat(await page.getByRole('region', { name: 'Patient identity' }).innerText()));
  s.log('  checklist on open (nothing done by the doctor yet): ' + await (async () => { await page.getByRole('tab', { name: /^Checklist/ }).click(); return flat(await page.getByRole('region', { name: 'Review and sign' }).first().innerText()); })());
  await s.shot('open-allergies-unknown');
  s.log('  sign button: enabled=' + await note.getByRole('button', { name: 'Sign note' }).isEnabled() + ' text near: ' + flat(await page.locator('#studio-sign').innerText().catch(() => '')));
});
await step('stale-after-quick-route-change', async () => {
  const a = note.getByRole('textbox', { name: /^Assessment/ });
  await a.fill('FIRST VERSION viral fever (synthetic)'); await page.waitForTimeout(3000);
  await a.fill('SECOND VERSION dengue suspected (synthetic)');
  await page.getByRole('link', { name: 'Problems and diagnoses' }).click(); await s.settle();
  await page.getByRole('link', { name: 'Back to the consultation' }).click(); await s.settle();
  const shown = await note.getByRole('textbox', { name: /^Assessment/ }).inputValue();
  await s.shot('after-quick-route-change');
  await page.reload(); await s.settle();
  const server = await note.getByRole('textbox', { name: /^Assessment/ }).inputValue();
  s.log(`  typed "SECOND VERSION…", left for Problems at once, came back: screen shows ${JSON.stringify(shown)}; after a reload (server) ${JSON.stringify(server)}`);
});
await step('two-tabs', async () => {
  const b = await session(browser, users.doctor, 'doctor', 'consult-2-second-tab');
  await b.goto(consult);
  const nb = b.page.getByRole('region', { name: 'Consultation note' });
  await note.getByRole('textbox', { name: /^Subjective/ }).fill('TAB-A text typed first (synthetic)'); await page.waitForTimeout(3000);
  await nb.getByRole('textbox', { name: /^Subjective/ }).fill('TAB-B text typed second without reloading (synthetic)'); await b.page.waitForTimeout(3000);
  b.log('  tab B statuses: ' + (await nb.getByRole('status').allInnerTexts()).join(' / ') + ' alerts: ' + (await b.page.getByRole('alert').allInnerTexts()).join(' / ')); await b.shot('tab-b-after-typing');
  await note.getByRole('textbox', { name: /^Objective/ }).fill('TAB-A objective typed third (synthetic)'); await page.waitForTimeout(3000);
  s.log('  tab A statuses: ' + (await note.getByRole('status').allInnerTexts()).join(' / ') + ' alerts: ' + (await page.getByRole('alert').allInnerTexts()).join(' / ')); await s.shot('tab-a-after-conflict');
  await page.reload(); await s.settle();
  s.log('  server now: Subjective=' + JSON.stringify(await note.getByRole('textbox', { name: /^Subjective/ }).inputValue()) + ' Objective=' + JSON.stringify(await note.getByRole('textbox', { name: /^Objective/ }).inputValue()));
  await b.close();
});
await step('complete-note', async () => {
  s.reset();
  await s.fill(note.getByRole('textbox', { name: /^Chief complaint/ }), 'Fever for two days (synthetic)', 'Chief complaint');
  await s.fill(note.getByRole('textbox', { name: /^Assessment/ }), 'Viral fever (synthetic)', 'Assessment');
  await s.fill(note.getByRole('textbox', { name: /^Plan/ }), 'Paracetamol, fluids, review if worse (synthetic)', 'Plan');
  await s.click(note.getByRole('button', { name: 'Confirm: no known allergies' }).first(), 'Confirm: no known allergies');
  await s.click(note.getByRole('button', { name: 'Add medicine' }).first(), 'Add medicine');
  const panel = page.getByRole('dialog', { name: 'Add medicine' });
  await s.fill(panel.getByLabel('Search medicine'), 'parac', 'search'); await page.waitForTimeout(1200);
  s.log('  matches: ' + (await panel.getByRole('button').allInnerTexts()).filter(x => /parac/i.test(x)).map(x => x.replace(/\n/g, ' ')).join(' ; '));
  await s.click(panel.getByRole('button', { name: /^Paracetamol/ }).first(), 'pick');
  for (const [g, o] of [['How often', '1-1-1 · three times a day'], ['When', 'After food'], ['For how long', '3 days']]) await s.click(panel.getByRole('group', { name: g }).getByRole('button', { name: o, exact: true }), `${g}: ${o}`);
  s.log('  line: ' + flat(await panel.getByRole('region', { name: 'Prescription line' }).innerText().catch(() => '')));
  await s.click(panel.getByRole('button', { name: 'Add to prescription' }), 'Add to prescription');
  s.counts('doctor: 3 note fields + allergy confirm + 1 medicine');
  s.reset();
  await openPart('Orders'); const orders = page.getByRole('region', { name: 'Orders' });
  s.log('  orders: ' + flat(await orders.innerText()).slice(0, 600));
  await s.fill(orders.getByLabel('Test', { exact: true }), 'CBC', 'Test'); await s.click(orders.getByRole('button', { name: 'Add test' }), 'Add test');
  await s.click(orders.getByRole('button', { name: 'Place orders' }), 'Place orders'); await s.shot('orders');
  s.log('  orders after: ' + flat(await orders.innerText()).slice(0, 600));
  await s.click(note.getByRole('button', { name: '2 weeks' }), 'Follow-up: 2 weeks'); await page.waitForTimeout(1200); await s.shot('follow-up');
  const t = flat(await note.innerText()); s.log('  follow-up: ' + t.slice(t.indexOf('Follow-up'), t.indexOf('Follow-up') + 400));
  await s.fill(note.getByRole('textbox', { name: /^Advice for the patient/ }), 'Drink fluids. Return if fever lasts beyond 3 days. (synthetic)', 'Advice'); await s.click(note.getByRole('button', { name: 'Save advice' }), 'Save advice');
  s.counts('doctor: order 1 test + follow-up + advice');
});
await step('authorise-then-sign', async () => {
  s.reset();
  await s.click(rx.getByRole('button', { name: 'Authorise prescription' }), 'Authorise prescription');
  s.log('  alert: ' + (await rx.getByRole('alert').allInnerTexts()).join(' / '));
  await s.click(rx.getByRole('button', { name: 'Confirm identity' }), 'Confirm identity'); await s.shot('step-up-provider'); await stepUp();
  s.log('  back from step-up: scrollY=' + await page.evaluate(() => Math.round(scrollY)) + ' rx in view=' + await rx.evaluate(e => { const r = e.getBoundingClientRect(); return r.top < innerHeight && r.bottom > 0; }));
  await s.click(rx.getByRole('button', { name: 'Authorise prescription' }), 'Authorise prescription (again)');
  s.log('  rx status: ' + (await rx.getByRole('status').allInnerTexts()).join(' / ')); await rx.scrollIntoViewIfNeeded(); await s.shot('authorised');
  s.log('  rx actions now: ' + (await rx.getByRole('button').allInnerTexts()).concat(await rx.getByRole('link').allInnerTexts()).filter(x => x.trim()).join(' / '));
  s.counts('doctor: authorise prescription (with step-up)');
  s.reset();
  const sign = note.getByRole('button', { name: 'Sign note' });
  s.log('  sign: enabled=' + await sign.isEnabled() + ' · ' + await sign.evaluate(e => document.getElementById(e.getAttribute('aria-describedby') || '')?.textContent));
  await s.click(sign, 'Sign note'); await s.click(page.getByRole('dialog').getByRole('button', { name: /^Sign as/ }), 'Sign as …'); await page.waitForTimeout(1200);
  const second = await page.getByRole('link', { name: users.doctor }).count();
  s.log('  second step-up asked for the note right after the prescription step-up? ' + (second ? 'YES' : 'no'));
  if (second) { await stepUp(); await s.click(sign, 'Sign note'); await s.click(page.getByRole('dialog').getByRole('button', { name: /^Sign as/ }), 'Sign as …'); }
  s.log('  signed: ' + (await note.getByText(/SIGNED v1/).count()));
  s.counts('doctor: sign note');
  await page.goto(page.url().replace(/\/$/, '') + '/rx/print'); await s.settle(); await s.shot('rx-print', true);
  s.log('  rx print: ' + flat(await s.text()).slice(0, 1500));
});
await step('desk-closes-mid-consult', async () => {
  await s.goto(consult);
  const d = await session(browser, users.receptionist, 'handoff', 'desk-closes-mid-consult');
  await d.goto('/front-desk'); const card = d.page.getByRole('article', { name: /Lalitha Raman/ });
  d.log('  desk card: ' + flat(await card.innerText())); await d.click(card.getByRole('button', { name: 'Consult done' }), 'Consult done (desk, while the doctor is still in the consultation)'); await d.shot('desk-pressed-consult-done');
  d.log('  card after: ' + flat(await d.page.getByRole('article', { name: /Lalitha Raman/ }).innerText())); await d.close();
  await page.waitForTimeout(2500); await s.shot('doctor-screen-after-desk-closed');
  s.log('  doctor screen top after the desk closed the visit (no reload): ' + flat(await s.text()).slice(0, 500));
  await page.reload(); await s.settle(); s.log('  after reload: ' + flat(await s.text()).slice(0, 400));
  const end = page.getByRole('button', { name: 'End consultation' }); s.log('  End consultation still offered: ' + await end.count());
});
await step('second-consult-while-open', async () => {
  await s.goto('/today'); s.reset();
  const k = page.getByRole('region', { name: "Today's visits" }).getByRole('article', { name: /Kavya Pillai/ });
  await s.click(k.getByRole('button', { name: /Start consultation/ }), 'Start consultation (Kavya) while Lalitha is open'); await s.shot('second-consult');
  s.log('  url: ' + page.url().replace(/[0-9a-f-]{36}/, ':id') + ' alerts: ' + (await page.getByRole('alert').allInnerTexts()).join(' / ') + ' text: ' + flat(await s.text()).slice(0, 500));
  if (/consult\//.test(page.url())) consult2 = new URL(page.url()).pathname.replace('/app', '');
  if (!consult2) { // end the first, then start Kavya
    await s.goto(consult); const end = page.getByRole('button', { name: 'End consultation' }).first(); if (await end.count()) await s.click(end, 'End consultation (Lalitha)');
    await s.goto('/today'); await s.click(page.getByRole('region', { name: "Today's visits" }).getByRole('article', { name: /Kavya Pillai/ }).getByRole('button', { name: /Start consultation/ }), 'Start consultation (Kavya)');
    consult2 = new URL(page.url()).pathname.replace('/app', ''); s.log('  second start url: ' + page.url().replace(/[0-9a-f-]{36}/, ':id'));
  }
});
await step('end-with-draft-rx', async () => {
  if (!consult2) return; await s.goto(consult2); s.reset();
  await s.click(note.getByRole('button', { name: 'Add medicine' }).first(), 'Add medicine'); const panel = page.getByRole('dialog', { name: 'Add medicine' });
  s.log('  dialog patient/allergy strip with allergies unknown: ' + flat(await panel.getByRole('region', { name: 'Patient' }).innerText()));
  await panel.getByLabel('Search medicine').fill('parac'); await page.waitForTimeout(1200); await panel.getByRole('button', { name: /^Paracetamol/ }).first().click();
  for (const [g, o] of [['How often', '1-1-1 · three times a day'], ['When', 'After food'], ['For how long', '3 days']]) await panel.getByRole('group', { name: g }).getByRole('button', { name: o, exact: true }).click();
  s.log('  safety with allergies not recorded: ' + flat(await panel.getByRole('region', { name: 'Safety checks' }).innerText()));
  await s.shot('add-medicine-allergy-unknown');
  await panel.getByRole('button', { name: 'Add to prescription' }).click(); await s.settle();
  await s.click(page.getByRole('button', { name: 'End consultation' }).first(), 'End consultation with a draft prescription'); await s.shot('ended-with-draft-rx');
  s.log('  dialogs: ' + await page.getByRole('dialog').count() + ' alerts: ' + (await page.getByRole('alert').allInnerTexts()).join(' / ') + ' statuses: ' + (await page.getByRole('status').allInnerTexts()).join(' / ').slice(0, 300));
  await rx.scrollIntoViewIfNeeded(); await s.shot('rx-after-end');
  s.log('  rx after end: ' + flat(await rx.innerText()).slice(0, 700));
  const auth = rx.getByRole('button', { name: 'Authorise prescription' });
  if (await auth.count()) { s.log('  authorise enabled after end: ' + await auth.isEnabled()); if (await auth.isEnabled()) { await s.click(auth, 'Authorise after end'); s.log('  result: ' + (await rx.getByRole('alert').allInnerTexts()).join(' / ') + ' | ' + (await rx.getByRole('status').allInnerTexts()).join(' / ')); const c = rx.getByRole('button', { name: 'Confirm identity' }); if (await c.count()) { await c.click(); await stepUp(); await rx.getByRole('button', { name: 'Authorise prescription' }).click(); await s.settle(); s.log('  after step-up: ' + (await rx.getByRole('alert').allInnerTexts()).join(' / ') + ' | ' + (await rx.getByRole('status').allInnerTexts()).join(' / ')); await rx.scrollIntoViewIfNeeded(); await s.shot('authorise-after-end'); } } }
  s.log('  is there a way to reopen the ended consultation? buttons: ' + (await page.locator('#main button').allInnerTexts()).filter(x => /reopen|resume|open again/i.test(x)).join(' / '));
  await s.goto('/today'); s.log('  Today notes to sign: ' + flat(await page.getByRole('region', { name: /notes? to sign$/ }).innerText().catch(() => 'none'))); await s.shot('today-notes-to-sign');
  s.log('  Today mentions unauthorised prescriptions? ' + /prescription/i.test(await s.text()));
});
await browser.close();
